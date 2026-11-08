#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>
/// Reader-source adapter for H3 map package directory containers. Validates manifest.json,
/// verifies layer and retained section binary payloads through <see cref="MapPackageChunkFileReader"/>,
/// and reconstructs a fully validated <see cref="MapInterchange"/> with <see cref="MapSourceKind.H3Package"/>.
/// </summary>
public sealed class PackageReader : IMapReader
{
    private static readonly Dictionary<string, MapNodataPolicy> ValidNodataPolicies = new(StringComparer.Ordinal)
    {
        [nameof(MapNodataPolicy.Fail)] = MapNodataPolicy.Fail,
        [nameof(MapNodataPolicy.NearestValid)] = MapNodataPolicy.NearestValid,
        [nameof(MapNodataPolicy.Constant)] = MapNodataPolicy.Constant,
    };

    private readonly string _packageRoot;

    public PackageReader(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        _packageRoot = packageRoot;
    }

    public string PackageRoot => _packageRoot;

    public MapInterchange Read()
    {
        var manifestPath = Path.Combine(_packageRoot, MapPackageFormat.ManifestFileName);
        var manifestInfo = new FileInfo(manifestPath);

        if (!manifestInfo.Exists)
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapPackageChunkDiagnostic.FileMissing,
                    $"Package manifest '{manifestPath}' does not exist."),
            ]);
        }

        if (manifestInfo.Length > MapPackageFormat.MaxManifestBytes)
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapPackageDiagnostic.ResourceLimitExceeded,
                    $"Package manifest '{manifestPath}' length {manifestInfo.Length} bytes exceeds the limit of {MapPackageFormat.MaxManifestBytes} bytes."),
            ]);
        }

        string json;

        try
        {
            json = File.ReadAllText(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapPackageChunkDiagnostic.IoFailure,
                    $"Reading package manifest '{manifestPath}' failed: {ex.Message}"),
            ]);
        }

        var (manifest, manifestDiagnostics) = MapPackageManifestJson.ParseAndValidate(json);

        if (manifest is null || manifestDiagnostics.Count > 0)
        {
            throw new MapImportException(
                manifestDiagnostics.Select(d => new MapInterchangeDiagnostic(d.Code, d.Message)).ToArray());
        }

        var layerLookup = new Dictionary<(int X, int Y, string Kind), MapPackageLayerEntry>();

        foreach (var entry in manifest.Layers)
        {
            layerLookup[(entry.ChunkX, entry.ChunkY, entry.LayerKind)] = entry;
        }

        var chunks = new List<Chunk>(manifest.Layout.ChunksX * manifest.Layout.ChunksY);

        for (var cy = 0; cy < manifest.Layout.ChunksY; cy++)
        {
            for (var cx = 0; cx < manifest.Layout.ChunksX; cx++)
            {
                if (!layerLookup.TryGetValue((cx, cy, MapPackageFormat.LayerKindHeights), out var heightsEntry))
                {
                    throw new MapImportException(
                    [
                        new MapInterchangeDiagnostic(
                            MapPackageDiagnostic.ChunkInventory,
                            $"Missing heights layer entry for chunk ({cx}, {cy})."),
                    ]);
                }

                var heightsResult = MapPackageChunkFileReader.ReadHeights(_packageRoot, heightsEntry);

                if (!heightsResult.Success)
                {
                    throw new MapImportException(
                        heightsResult.Diagnostics.Select(d => new MapInterchangeDiagnostic(d.Code, d.Message)).ToArray());
                }

                var heights = new HeightLayer(heightsResult.Payload);

                MaterialLayer? materials = null;

                if (layerLookup.TryGetValue((cx, cy, MapPackageFormat.LayerKindMaterials), out var materialsEntry))
                {
                    var materialsResult = MapPackageChunkFileReader.ReadMaterials(_packageRoot, materialsEntry);

                    if (!materialsResult.Success)
                    {
                        throw new MapImportException(
                            materialsResult.Diagnostics.Select(d => new MapInterchangeDiagnostic(d.Code, d.Message)).ToArray());
                    }

                    materials = new MaterialLayer(materialsResult.Payload);
                }

                MaskLayer? masks = null;

                if (layerLookup.TryGetValue((cx, cy, MapPackageFormat.LayerKindMasks), out var masksEntry))
                {
                    var masksResult = MapPackageChunkFileReader.ReadMasks(_packageRoot, masksEntry);

                    if (!masksResult.Success)
                    {
                        throw new MapImportException(
                            masksResult.Diagnostics.Select(d => new MapInterchangeDiagnostic(d.Code, d.Message)).ToArray());
                    }

                    masks = new MaskLayer(masksResult.Payload);
                }

                ImageryLayer? imagery = null;

                if (layerLookup.TryGetValue((cx, cy, MapPackageFormat.LayerKindImagery), out var imageryEntry))
                {
                    var imageryResult = MapPackageChunkFileReader.ReadImagery(_packageRoot, imageryEntry);

                    if (!imageryResult.Success)
                    {
                        throw new MapImportException(
                            imageryResult.Diagnostics.Select(d => new MapInterchangeDiagnostic(d.Code, d.Message)).ToArray());
                    }

                    imagery = new ImageryLayer(imageryEntry.Width, imageryEntry.Height, imageryResult.Payload);
                }

                chunks.Add(new Chunk(new ChunkKey(cx, cy), heights, materials, masks, imagery));
            }
        }

        var retainedSections = new List<RetainedSection>(manifest.RetainedSections.Count);

        foreach (var retainedEntry in manifest.RetainedSections)
        {
            var retainedResult = MapPackageChunkFileReader.ReadRetained(_packageRoot, retainedEntry);

            if (!retainedResult.Success)
            {
                throw new MapImportException(
                    retainedResult.Diagnostics.Select(d => new MapInterchangeDiagnostic(d.Code, d.Message)).ToArray());
            }

            retainedSections.Add(new RetainedSection(
                retainedEntry.SectionName,
                retainedEntry.SectionVersion,
                retainedResult.Payload,
                retainedEntry.Sha256));
        }

        if (!manifest.Layout.SampleSpacing.TryToMapQuantity(out var sampleSpacing))
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapPackageDiagnostic.QuantityUnitFrame,
                    $"Invalid sample spacing quantity in layout manifest: {manifest.Layout.SampleSpacing}."),
            ]);
        }

        if (manifest.Layout.NodataPolicy is null || !ValidNodataPolicies.TryGetValue(manifest.Layout.NodataPolicy, out var nodataPolicy))
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapPackageDiagnostic.NodataPolicy,
                    $"Invalid NodataPolicy '{manifest.Layout.NodataPolicy}' in package manifest; expected Fail, NearestValid, or Constant."),
            ]);
        }

        var geospatialTransform = manifest.Layout.GeospatialTransform?.ToModel();

#pragma warning disable CS8604
        var terrainLayout = new TerrainLayout(
            sampleSpacing,
            manifest.Layout.LatticeWidth,
            manifest.Layout.LatticeHeight,
            manifest.Layout.ChunksX,
            manifest.Layout.ChunksY,
            manifest.Layout.Border,
            geospatialTransform,
            nodataPolicy,
            manifest.Layout.HeightSectionVersion,
            manifest.Layout.MapArea,
            manifest.Layout.TerrainBorders.Select(b => b.ToModel()).ToArray(),
            chunks);
#pragma warning restore CS8604

        var identity = new MapIdentity(
            manifest.Identity.Name,
            MapSourceKind.H3Package,
            manifest.Identity.LegacyGlobalVersion);

        var blend = manifest.Blend.ToModel();

        var objects = manifest.Objects is not null
            ? new PlacedObjectSet(manifest.Objects.Select(o => o.ToModel()).ToArray())
            : null;

        var waypoints = manifest.Waypoints is not null
            ? new WaypointSet(manifest.Waypoints.Select(w => w.ToModel()).ToArray())
            : null;

        var provenance = manifest.Provenance.ToModel();
        var report = new ImportReport([], [], 0, 0, []);

        var model = new MapInterchange(
            manifest.SchemaVersion,
            identity,
            terrainLayout,
            blend,
            objects,
            waypoints,
            provenance,
            report,
            retainedSections);

        var validationDiagnostics = MapInterchangeValidator.Validate(model);

        if (validationDiagnostics.Count > 0)
        {
            throw new MapImportException(validationDiagnostics);
        }

        return model;
    }
}
