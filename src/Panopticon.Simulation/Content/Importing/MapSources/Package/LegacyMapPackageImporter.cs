#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>
/// Importer from <see cref="MapInterchange"/> to an H3 map package directory container.
/// Maps every interchange component into package manifest DTOs and staged chunk files,
/// atomic-commits the package through <see cref="MapPackageDirectoryWriter"/>, and
/// preserves authored data faithfully without quantizing, converting, or dropping.
/// </summary>
public sealed class LegacyMapPackageImporter
{
    private readonly MapInterchange _interchange;
    private readonly string _targetPackageRoot;

    public LegacyMapPackageImporter(MapInterchange interchange, string targetPackageRoot)
    {
        ArgumentNullException.ThrowIfNull(interchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPackageRoot);
        _interchange = interchange;
        _targetPackageRoot = targetPackageRoot;
    }

    public MapPackageWriteResult Import() => Import(_interchange, _targetPackageRoot);

    public static MapPackageWriteResult Write(MapInterchange interchange, string targetPackageRoot) =>
        Import(interchange, targetPackageRoot);

    public static MapPackageWriteResult Import(MapInterchange interchange, string targetPackageRoot)
    {
        ArgumentNullException.ThrowIfNull(interchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPackageRoot);

        var interchangeDiagnostics = MapInterchangeValidator.Validate(interchange);

        if (interchangeDiagnostics.Count > 0)
        {
            return new MapPackageWriteResult(
                false,
                interchangeDiagnostics.Select(d => new MapPackageDiagnostic(d.Code, d.Message)).ToArray());
        }

        var identity = MapPackageIdentity.FromModel(interchange.Identity);
        var layout = MapPackageTerrainLayout.FromModel(interchange.Layout);
        var blend = MapPackageBlendModel.FromModel(interchange.Blend);
        var objects = interchange.Objects?.Objects.Select(MapPackagePlacedObject.FromModel).ToArray();
        var waypoints = interchange.Waypoints?.Waypoints.Select(MapPackageWaypoint.FromModel).ToArray();
        var provenance = MapPackageProvenance.FromModel(interchange.Provenance);

        var chunkLookup = new Dictionary<(int X, int Y), Chunk>(interchange.Layout.Chunks.Count);

        foreach (var chunk in interchange.Layout.Chunks)
        {
            chunkLookup[(chunk.Key.Cx, chunk.Key.Cy)] = chunk;
        }

        var layers = new List<MapPackageLayerEntry>();

        for (var cy = 0; cy < interchange.Layout.ChunksY; cy++)
        {
            for (var cx = 0; cx < interchange.Layout.ChunksX; cx++)
            {
                var chunk = chunkLookup[(cx, cy)];
                var coreW = Math.Min(MapChunkGeometry.CorePointsPerAxis, interchange.Layout.LatticeWidth - (cx * MapChunkGeometry.IntervalsPerChunkAxis));
                var coreH = Math.Min(MapChunkGeometry.CorePointsPerAxis, interchange.Layout.LatticeHeight - (cy * MapChunkGeometry.IntervalsPerChunkAxis));

                var heightsWidth = coreW + (2 * MapChunkGeometry.ApronSamplesPerSide);
                var heightsHeight = coreH + (2 * MapChunkGeometry.ApronSamplesPerSide);
                var heightsEncoded = MapPackageChunkCodec.EncodeHeights(chunk.Heights.Samples.ToArray());
                var heightsDigest = ComputeSha256Hex(heightsEncoded);

                layers.Add(new MapPackageLayerEntry(
                    cx,
                    cy,
                    MapPackageFormat.LayerKindHeights,
                    $"layers/heights_{cx}_{cy}.bin",
                    MapPackageFormat.EncodingFloat32Le,
                    heightsWidth,
                    heightsHeight,
                    heightsEncoded.Length,
                    heightsDigest));

                if (chunk.Materials is not null)
                {
                    var materialsEncoded = MapPackageChunkCodec.EncodeMaterials(chunk.Materials.Indices.ToArray());
                    var materialsDigest = ComputeSha256Hex(materialsEncoded);

                    layers.Add(new MapPackageLayerEntry(
                        cx,
                        cy,
                        MapPackageFormat.LayerKindMaterials,
                        $"layers/materials_{cx}_{cy}.bin",
                        MapPackageFormat.EncodingUint8,
                        coreW,
                        coreH,
                        materialsEncoded.Length,
                        materialsDigest));
                }

                if (chunk.Masks is not null)
                {
                    var masksEncoded = MapPackageChunkCodec.EncodeMasks(chunk.Masks.Flags.ToArray());
                    var masksDigest = ComputeSha256Hex(masksEncoded);

                    layers.Add(new MapPackageLayerEntry(
                        cx,
                        cy,
                        MapPackageFormat.LayerKindMasks,
                        $"layers/masks_{cx}_{cy}.bin",
                        MapPackageFormat.EncodingUint8,
                        coreW,
                        coreH,
                        masksEncoded.Length,
                        masksDigest));
                }

                if (chunk.Imagery is not null)
                {
                    var imageryEncoded = MapPackageChunkCodec.EncodeImagery(chunk.Imagery.Rgba.ToArray());
                    var imageryDigest = ComputeSha256Hex(imageryEncoded);

                    layers.Add(new MapPackageLayerEntry(
                        cx,
                        cy,
                        MapPackageFormat.LayerKindImagery,
                        $"layers/imagery_{cx}_{cy}.raw",
                        MapPackageFormat.EncodingRgba8,
                        chunk.Imagery.Width,
                        chunk.Imagery.Height,
                        imageryEncoded.Length,
                        imageryDigest));
                }
            }
        }

        var retainedLookup = new Dictionary<string, RetainedSection>(interchange.RetainedSections.Count, StringComparer.Ordinal);
        var retainedEntries = new List<MapPackageRetainedEntry>(interchange.RetainedSections.Count);

        foreach (var section in interchange.RetainedSections)
        {
            retainedLookup[section.SectionName] = section;
            var digest = ComputeSha256Hex(section.Bytes.ToArray());

            retainedEntries.Add(new MapPackageRetainedEntry(
                section.SectionName,
                section.SectionVersion,
                $"retained/{section.SectionName}.bin",
                section.Bytes.Length,
                digest));
        }

        var manifest = new MapPackageManifest(
            MapPackageFormat.PackageFormatName,
            MapPackageFormat.CurrentPackageVersion,
            interchange.SchemaVersion,
            identity,
            layout,
            blend,
            objects,
            waypoints,
            provenance,
            layers,
            retainedEntries);

        var payloadProvider = new InterchangePayloadProvider(chunkLookup, retainedLookup);

        return MapPackageDirectoryWriter.Write(targetPackageRoot, manifest, payloadProvider);
    }

    private static string ComputeSha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class InterchangePayloadProvider : IMapPackagePayloadProvider
    {
        private readonly Dictionary<(int X, int Y), Chunk> _chunks;
        private readonly Dictionary<string, RetainedSection> _retained;

        public InterchangePayloadProvider(
            Dictionary<(int X, int Y), Chunk> chunks,
            Dictionary<string, RetainedSection> retained)
        {
            _chunks = chunks;
            _retained = retained;
        }

        public float[] GetHeights(MapPackageLayerEntry entry)
        {
            var chunk = _chunks[(entry.ChunkX, entry.ChunkY)];
            return chunk.Heights.Samples is float[] arr ? arr : chunk.Heights.Samples.ToArray();
        }

        public byte[] GetMaterials(MapPackageLayerEntry entry)
        {
            var chunk = _chunks[(entry.ChunkX, entry.ChunkY)];
            return chunk.Materials!.Indices is byte[] arr ? arr : chunk.Materials.Indices.ToArray();
        }

        public byte[] GetMasks(MapPackageLayerEntry entry)
        {
            var chunk = _chunks[(entry.ChunkX, entry.ChunkY)];
            return chunk.Masks!.Flags is byte[] arr ? arr : chunk.Masks.Flags.ToArray();
        }

        public byte[] GetImagery(MapPackageLayerEntry entry)
        {
            var chunk = _chunks[(entry.ChunkX, entry.ChunkY)];
            return chunk.Imagery!.Rgba is byte[] arr ? arr : chunk.Imagery.Rgba.ToArray();
        }

        public byte[] GetRetainedSection(MapPackageRetainedEntry entry)
        {
            var section = _retained[entry.SectionName];
            return section.Bytes.ToArray();
        }
    }
}
