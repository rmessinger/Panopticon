#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Schema-level validator for map package manifests.</summary>
public static class MapPackageManifestValidator
{
    public static IReadOnlyList<MapPackageDiagnostic> Validate(MapPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var diagnostics = new List<MapPackageDiagnostic>();

        ValidateFormatAndVersions(manifest, diagnostics);
        RequireComponent(manifest.Identity, nameof(MapPackageManifest.Identity), diagnostics);
        RequireComponent(manifest.Layout, nameof(MapPackageManifest.Layout), diagnostics);
        RequireComponent(manifest.Blend, nameof(MapPackageManifest.Blend), diagnostics);
        RequireComponent(manifest.Provenance, nameof(MapPackageManifest.Provenance), diagnostics);
        RequireComponent(manifest.Layers, nameof(MapPackageManifest.Layers), diagnostics);
        RequireComponent(manifest.RetainedSections, nameof(MapPackageManifest.RetainedSections), diagnostics);

        ValidateIdentity(manifest.Identity, diagnostics);
        ValidateLatticeDimensions(manifest.Layout, diagnostics);
        ValidateNodataPolicy(manifest.Layout, diagnostics);

        var sized = manifest.Layout is { } layout && layout.LatticeWidth > 0 && layout.LatticeHeight > 0
            ? layout
            : null;

        ValidateLayers(sized, manifest.Layers, diagnostics);
        ValidatePalette(manifest.Blend, diagnostics);
        ValidateQuantities(manifest, diagnostics);

        var stableIds = new HashSet<string>(StringComparer.Ordinal);
        ValidatePlacements(manifest, stableIds, diagnostics);
        ValidateWaypoints(manifest, stableIds, diagnostics);
        ValidateProvenance(manifest, stableIds, diagnostics);
        ValidateRetainedSections(manifest, diagnostics);

        if (diagnostics.Count > 100)
        {
            var suppressed = diagnostics.Count - 100;
            diagnostics.RemoveRange(100, suppressed);
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.DiagnosticCapExceeded,
                $"Diagnostic count exceeded limit of 100; {suppressed} additional diagnostics were suppressed."));
        }

        return diagnostics;
    }

    private static void ValidateFormatAndVersions(MapPackageManifest manifest, List<MapPackageDiagnostic> diagnostics)
    {
        if (!string.Equals(manifest.Format, MapPackageFormat.PackageFormatName, StringComparison.Ordinal))
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.PackageFormat,
                $"Package format '{manifest.Format}' is not the expected format '{MapPackageFormat.PackageFormatName}'."));
        }

        if (manifest.PackageVersion != MapPackageFormat.CurrentPackageVersion)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.PackageVersion,
                $"Package version {manifest.PackageVersion} is not supported; expected version {MapPackageFormat.CurrentPackageVersion}."));
        }

        if (manifest.SchemaVersion != MapInterchange.CurrentSchemaVersion)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.SchemaVersion,
                $"SchemaVersion {manifest.SchemaVersion} is not the current interchange schema version {MapInterchange.CurrentSchemaVersion}."));
        }
    }

    private static void RequireComponent(object? component, string name, List<MapPackageDiagnostic> diagnostics)
    {
        if (component is not null)
        {
            return;
        }

        diagnostics.Add(new MapPackageDiagnostic(
            MapPackageDiagnostic.NullComponent,
            $"Required component '{name}' is null; its dependent checks are skipped."));
    }

    private static readonly HashSet<string> ValidSourceKinds = new(StringComparer.Ordinal)
    {
        nameof(MapSourceKind.LegacyMap),
        nameof(MapSourceKind.H3Package),
        nameof(MapSourceKind.Generated),
    };

    private static readonly HashSet<string> ValidNodataPolicies = new(StringComparer.Ordinal)
    {
        nameof(MapNodataPolicy.Fail),
        nameof(MapNodataPolicy.NearestValid),
        nameof(MapNodataPolicy.Constant),
    };

    /// <summary>
    /// Validates that Identity.SourceKind names an exact MapSourceKind enum value.
    /// The field represents authored provenance: the manifest records the source the package content was authored from,
    /// while a reader returns MapSourceKind.H3Package for its own read.
    /// </summary>
    private static void ValidateIdentity(MapPackageIdentity? identity, List<MapPackageDiagnostic> diagnostics)
    {
        if (identity is null)
        {
            return;
        }

        if (identity.SourceKind is null || !ValidSourceKinds.Contains(identity.SourceKind))
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.IdentitySourceKind,
                $"Identity.SourceKind '{identity.SourceKind}' is not a valid MapSourceKind name; expected one of: {string.Join(", ", ValidSourceKinds)}."));
        }
    }

    private static void ValidateNodataPolicy(MapPackageTerrainLayout? layout, List<MapPackageDiagnostic> diagnostics)
    {
        if (layout is null)
        {
            return;
        }

        if (layout.NodataPolicy is null || !ValidNodataPolicies.Contains(layout.NodataPolicy))
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.NodataPolicy,
                $"TerrainLayout.NodataPolicy '{layout.NodataPolicy}' is not a valid MapNodataPolicy name; expected Fail, NearestValid, or Constant."));
        }
    }

    private static void ValidateLatticeDimensions(MapPackageTerrainLayout? layout, List<MapPackageDiagnostic> diagnostics)
    {
        if (layout is null)
        {
            return;
        }

        if (layout.LatticeWidth <= 0 || layout.LatticeHeight <= 0)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.LatticeDimensions,
                $"TerrainLayout lattice is {layout.LatticeWidth} x {layout.LatticeHeight} points; both must be positive."));
            return;
        }

        if (layout.LatticeWidth > MapPackageFormat.MaxLatticePointsPerAxis)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.ResourceLimitExceeded,
                $"TerrainLayout LatticeWidth {layout.LatticeWidth} exceeds the limit of {MapPackageFormat.MaxLatticePointsPerAxis} points per axis."));
        }

        if (layout.LatticeHeight > MapPackageFormat.MaxLatticePointsPerAxis)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.ResourceLimitExceeded,
                $"TerrainLayout LatticeHeight {layout.LatticeHeight} exceeds the limit of {MapPackageFormat.MaxLatticePointsPerAxis} points per axis."));
        }

        var totalLatticePoints = (long)layout.LatticeWidth * layout.LatticeHeight;

        if (totalLatticePoints > MapPackageFormat.MaxLatticePointsTotal)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.ResourceLimitExceeded,
                $"TerrainLayout total lattice points {totalLatticePoints} exceeds the limit of {MapPackageFormat.MaxLatticePointsTotal} points."));
        }

        var expectedChunksX = (layout.LatticeWidth - 1L + MapChunkGeometry.IntervalsPerChunkAxis - 1) / MapChunkGeometry.IntervalsPerChunkAxis;
        var expectedChunksY = (layout.LatticeHeight - 1L + MapChunkGeometry.IntervalsPerChunkAxis - 1) / MapChunkGeometry.IntervalsPerChunkAxis;

        if (layout.ChunksX == expectedChunksX && layout.ChunksY == expectedChunksY)
        {
            return;
        }

        diagnostics.Add(new MapPackageDiagnostic(
            MapPackageDiagnostic.LatticeDimensions,
            $"Chunk grid {layout.ChunksX} x {layout.ChunksY} does not tile the {layout.LatticeWidth} x {layout.LatticeHeight} lattice; the grid requires {expectedChunksX} x {expectedChunksY}."));
    }

    private static void ValidateLayers(
        MapPackageTerrainLayout? layout,
        IReadOnlyList<MapPackageLayerEntry>? layers,
        List<MapPackageDiagnostic> diagnostics)
    {
        if (layout is null || layers is null)
        {
            return;
        }

        var gridValid = layout.ChunksX > 0 && layout.ChunksY > 0 && layout.LatticeWidth > 0 && layout.LatticeHeight > 0;
        long expectedChunkCount;

        try
        {
            expectedChunkCount = checked((long)layout.ChunksX * layout.ChunksY);
        }
        catch (OverflowException)
        {
            expectedChunkCount = long.MaxValue;
        }

        var heightsCount = 0L;
        var materialsCount = 0L;
        var masksCount = 0L;
        var imageryCount = 0L;
        var heightsCoords = new HashSet<(int Cx, int Cy)>();
        int? imageryWidth = null;
        int? imageryHeight = null;

        for (var i = 0; i < layers.Count; i++)
        {
            var entry = layers[i];

            if (entry is null)
            {
                RequireComponent(entry, $"Layers[{i}]", diagnostics);
                continue;
            }

            if (!MapPackagePath.TryValidatePath(entry.Path, out var pathReason))
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.PathInvalid,
                    $"Layer entry ({entry.ChunkX}, {entry.ChunkY}, {entry.LayerKind}) path is invalid: {pathReason}"));
            }

            if (!MapPackagePath.IsValidSha256(entry.Sha256))
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.DigestInvalid,
                    $"Layer entry ({entry.ChunkX}, {entry.ChunkY}, {entry.LayerKind}) SHA-256 '{entry.Sha256}' must be 64 lowercase hex characters."));
            }

            int bytesPerSample;

            switch (entry.LayerKind)
            {
                case MapPackageFormat.LayerKindHeights:
                    heightsCount++;
                    if (gridValid && entry.ChunkX >= 0 && entry.ChunkX < layout.ChunksX && entry.ChunkY >= 0 && entry.ChunkY < layout.ChunksY)
                    {
                        heightsCoords.Add((entry.ChunkX, entry.ChunkY));
                    }

                    if (entry.Encoding != MapPackageFormat.EncodingFloat32Le)
                    {
                        diagnostics.Add(new MapPackageDiagnostic(
                            MapPackageDiagnostic.LayerEncoding,
                            $"Heights layer ({entry.ChunkX}, {entry.ChunkY}) declares encoding '{entry.Encoding}'; expected '{MapPackageFormat.EncodingFloat32Le}'."));
                    }

                    bytesPerSample = 4;
                    break;

                case MapPackageFormat.LayerKindMaterials:
                    materialsCount++;
                    if (entry.Encoding != MapPackageFormat.EncodingUint8)
                    {
                        diagnostics.Add(new MapPackageDiagnostic(
                            MapPackageDiagnostic.LayerEncoding,
                            $"Materials layer ({entry.ChunkX}, {entry.ChunkY}) declares encoding '{entry.Encoding}'; expected '{MapPackageFormat.EncodingUint8}'."));
                    }

                    bytesPerSample = 1;
                    break;

                case MapPackageFormat.LayerKindMasks:
                    masksCount++;
                    if (entry.Encoding != MapPackageFormat.EncodingUint8)
                    {
                        diagnostics.Add(new MapPackageDiagnostic(
                            MapPackageDiagnostic.LayerEncoding,
                            $"Masks layer ({entry.ChunkX}, {entry.ChunkY}) declares encoding '{entry.Encoding}'; expected '{MapPackageFormat.EncodingUint8}'."));
                    }

                    bytesPerSample = 1;
                    break;

                case MapPackageFormat.LayerKindImagery:
                    imageryCount++;
                    if (entry.Encoding != MapPackageFormat.EncodingRgba8)
                    {
                        diagnostics.Add(new MapPackageDiagnostic(
                            MapPackageDiagnostic.LayerEncoding,
                            $"Imagery layer ({entry.ChunkX}, {entry.ChunkY}) declares encoding '{entry.Encoding}'; expected '{MapPackageFormat.EncodingRgba8}'."));
                    }

                    bytesPerSample = 4;
                    break;

                default:
                    diagnostics.Add(new MapPackageDiagnostic(
                        MapPackageDiagnostic.LayerEncoding,
                        $"Layer entry ({entry.ChunkX}, {entry.ChunkY}) declares unknown layer kind '{entry.LayerKind}'."));
                    bytesPerSample = 0;
                    break;
            }

            if (entry.Width <= 0 || entry.Height <= 0)
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.LayerLength,
                    $"Layer entry ({entry.ChunkX}, {entry.ChunkY}, {entry.LayerKind}) declares non-positive dimensions {entry.Width} x {entry.Height}."));
            }
            else if (bytesPerSample > 0)
            {
                long expectedByteLength;

                try
                {
                    expectedByteLength = checked((long)entry.Width * entry.Height * bytesPerSample);
                }
                catch (OverflowException)
                {
                    expectedByteLength = -1;
                }

                if (entry.ByteLength != expectedByteLength)
                {
                    diagnostics.Add(new MapPackageDiagnostic(
                        MapPackageDiagnostic.LayerLength,
                        $"Layer entry ({entry.ChunkX}, {entry.ChunkY}, {entry.LayerKind}) declares byte length {entry.ByteLength}; expected {expectedByteLength} ({entry.Width} x {entry.Height} x {bytesPerSample} bytes)."));
                }
            }

            if (entry.LayerKind == MapPackageFormat.LayerKindImagery)
            {
                if (!imageryWidth.HasValue)
                {
                    imageryWidth = entry.Width;
                    imageryHeight = entry.Height;
                }
                else if (entry.Width != imageryWidth || entry.Height != imageryHeight)
                {
                    diagnostics.Add(new MapPackageDiagnostic(
                        MapPackageDiagnostic.LayerLength,
                        $"Chunk ({entry.ChunkX}, {entry.ChunkY}) imagery is {entry.Width} x {entry.Height}; expected consistent dimensions {imageryWidth} x {imageryHeight}."));
                }
            }
        }

        if (!gridValid)
        {
            return;
        }

        if (heightsCount != expectedChunkCount)
        {
            var missingSample = new List<string>(8);
            for (var cy = 0; cy < layout.ChunksY && missingSample.Count < 8; cy++)
            {
                for (var cx = 0; cx < layout.ChunksX && missingSample.Count < 8; cx++)
                {
                    if (!heightsCoords.Contains((cx, cy)))
                    {
                        missingSample.Add($"({cx}, {cy})");
                    }
                }
            }

            var totalMissing = expectedChunkCount > heightsCoords.Count
                ? expectedChunkCount - heightsCoords.Count
                : 0L;
            var remaining = totalMissing > missingSample.Count
                ? totalMissing - missingSample.Count
                : 0L;
            var moreSuffix = remaining > 0 ? $" and {remaining} more" : string.Empty;

            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.ChunkInventory,
                $"Heights layer entry count {heightsCount} does not match expected chunk count {expectedChunkCount} ({layout.ChunksX} x {layout.ChunksY}); missing coordinates: {string.Join(", ", missingSample)}{moreSuffix}."));
        }
        else
        {
            var seenHeights = new HashSet<(int Cx, int Cy)>();
            var duplicateHeights = 0;
            var outOfGridHeights = 0;

            for (var i = 0; i < layers.Count; i++)
            {
                var entry = layers[i];
                if (entry is null || entry.LayerKind != MapPackageFormat.LayerKindHeights)
                {
                    continue;
                }

                if (entry.ChunkX < 0 || entry.ChunkX >= layout.ChunksX || entry.ChunkY < 0 || entry.ChunkY >= layout.ChunksY)
                {
                    if (outOfGridHeights < 8)
                    {
                        diagnostics.Add(new MapPackageDiagnostic(
                            MapPackageDiagnostic.ChunkInventory,
                            $"Layer entry ({entry.ChunkX}, {entry.ChunkY}, heights) lies outside the {layout.ChunksX} x {layout.ChunksY} chunk grid."));
                    }

                    outOfGridHeights++;
                    continue;
                }

                if (!seenHeights.Add((entry.ChunkX, entry.ChunkY)))
                {
                    if (duplicateHeights < 8)
                    {
                        diagnostics.Add(new MapPackageDiagnostic(
                            MapPackageDiagnostic.ChunkInventory,
                            $"Duplicate heights layer entry for chunk ({entry.ChunkX}, {entry.ChunkY})."));
                    }

                    duplicateHeights++;
                }

                var coreW = Math.Min(MapChunkGeometry.CorePointsPerAxis, layout.LatticeWidth - (entry.ChunkX * MapChunkGeometry.IntervalsPerChunkAxis));
                var coreH = Math.Min(MapChunkGeometry.CorePointsPerAxis, layout.LatticeHeight - (entry.ChunkY * MapChunkGeometry.IntervalsPerChunkAxis));
                var expectedHeightW = coreW + (2 * MapChunkGeometry.ApronSamplesPerSide);
                var expectedHeightH = coreH + (2 * MapChunkGeometry.ApronSamplesPerSide);

                if (entry.Width != expectedHeightW || entry.Height != expectedHeightH)
                {
                    diagnostics.Add(new MapPackageDiagnostic(
                        MapPackageDiagnostic.LayerLength,
                        $"Chunk ({entry.ChunkX}, {entry.ChunkY}) heights declares {entry.Width} x {entry.Height}; expected {expectedHeightW} x {expectedHeightH}."));
                }
            }

            if (duplicateHeights > 8)
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.ChunkInventory,
                    $"{duplicateHeights - 8} additional duplicate heights entries were suppressed."));
            }

            if (outOfGridHeights > 8)
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.ChunkInventory,
                    $"{outOfGridHeights - 8} additional out-of-grid heights entries were suppressed."));
            }

            if (seenHeights.Count < expectedChunkCount)
            {
                var missingSample = new List<string>(8);
                for (var cy = 0; cy < layout.ChunksY && missingSample.Count < 8; cy++)
                {
                    for (var cx = 0; cx < layout.ChunksX && missingSample.Count < 8; cx++)
                    {
                        if (!seenHeights.Contains((cx, cy)))
                        {
                            missingSample.Add($"({cx}, {cy})");
                        }
                    }
                }

                var remaining = (expectedChunkCount - seenHeights.Count) - missingSample.Count;
                var moreSuffix = remaining > 0 ? $" and {remaining} more" : string.Empty;
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.ChunkInventory,
                    $"Chunk grid is missing required heights layer entries; missing coordinates: {string.Join(", ", missingSample)}{moreSuffix}."));
            }
        }

        if (materialsCount > 0 && materialsCount != expectedChunkCount)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.LayersConsistency,
                $"{materialsCount} of {expectedChunkCount} chunks carry materials; every chunk must carry materials or none may."));
        }
        else if (materialsCount == expectedChunkCount)
        {
            var seenMaterials = new HashSet<(int Cx, int Cy)>();
            for (var i = 0; i < layers.Count; i++)
            {
                var entry = layers[i];
                if (entry is null || entry.LayerKind != MapPackageFormat.LayerKindMaterials)
                {
                    continue;
                }

                if (entry.ChunkX < 0 || entry.ChunkX >= layout.ChunksX || entry.ChunkY < 0 || entry.ChunkY >= layout.ChunksY)
                {
                    diagnostics.Add(new MapPackageDiagnostic(
                        MapPackageDiagnostic.ChunkInventory,
                        $"Layer entry ({entry.ChunkX}, {entry.ChunkY}, materials) lies outside the {layout.ChunksX} x {layout.ChunksY} chunk grid."));
                    continue;
                }

                if (!seenMaterials.Add((entry.ChunkX, entry.ChunkY)))
                {
                    diagnostics.Add(new MapPackageDiagnostic(
                        MapPackageDiagnostic.ChunkInventory,
                        $"Duplicate materials layer entry for chunk ({entry.ChunkX}, {entry.ChunkY})."));
                }

                var coreW = Math.Min(MapChunkGeometry.CorePointsPerAxis, layout.LatticeWidth - (entry.ChunkX * MapChunkGeometry.IntervalsPerChunkAxis));
                var coreH = Math.Min(MapChunkGeometry.CorePointsPerAxis, layout.LatticeHeight - (entry.ChunkY * MapChunkGeometry.IntervalsPerChunkAxis));

                if (entry.Width != coreW || entry.Height != coreH)
                {
                    diagnostics.Add(new MapPackageDiagnostic(
                        MapPackageDiagnostic.LayerLength,
                        $"Chunk ({entry.ChunkX}, {entry.ChunkY}) materials declares {entry.Width} x {entry.Height}; expected {coreW} x {coreH}."));
                }
            }
        }

        if (masksCount > 0 && masksCount != expectedChunkCount)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.LayersConsistency,
                $"{masksCount} of {expectedChunkCount} chunks carry masks; every chunk must carry masks or none may."));
        }
        else if (masksCount == expectedChunkCount)
        {
            var seenMasks = new HashSet<(int Cx, int Cy)>();
            for (var i = 0; i < layers.Count; i++)
            {
                var entry = layers[i];
                if (entry is null || entry.LayerKind != MapPackageFormat.LayerKindMasks)
                {
                    continue;
                }

                if (entry.ChunkX < 0 || entry.ChunkX >= layout.ChunksX || entry.ChunkY < 0 || entry.ChunkY >= layout.ChunksY)
                {
                    diagnostics.Add(new MapPackageDiagnostic(
                        MapPackageDiagnostic.ChunkInventory,
                        $"Layer entry ({entry.ChunkX}, {entry.ChunkY}, masks) lies outside the {layout.ChunksX} x {layout.ChunksY} chunk grid."));
                    continue;
                }

                if (!seenMasks.Add((entry.ChunkX, entry.ChunkY)))
                {
                    diagnostics.Add(new MapPackageDiagnostic(
                        MapPackageDiagnostic.ChunkInventory,
                        $"Duplicate masks layer entry for chunk ({entry.ChunkX}, {entry.ChunkY})."));
                }

                var coreW = Math.Min(MapChunkGeometry.CorePointsPerAxis, layout.LatticeWidth - (entry.ChunkX * MapChunkGeometry.IntervalsPerChunkAxis));
                var coreH = Math.Min(MapChunkGeometry.CorePointsPerAxis, layout.LatticeHeight - (entry.ChunkY * MapChunkGeometry.IntervalsPerChunkAxis));

                if (entry.Width != coreW || entry.Height != coreH)
                {
                    diagnostics.Add(new MapPackageDiagnostic(
                        MapPackageDiagnostic.LayerLength,
                        $"Chunk ({entry.ChunkX}, {entry.ChunkY}) masks declares {entry.Width} x {entry.Height}; expected {coreW} x {coreH}."));
                }
            }
        }

        if (imageryCount > 0 && gridValid)
        {
            var seenImagery = new HashSet<(int Cx, int Cy)>();
            for (var i = 0; i < layers.Count; i++)
            {
                var entry = layers[i];
                if (entry is null || entry.LayerKind != MapPackageFormat.LayerKindImagery)
                {
                    continue;
                }

                if (entry.ChunkX < 0 || entry.ChunkX >= layout.ChunksX || entry.ChunkY < 0 || entry.ChunkY >= layout.ChunksY)
                {
                    diagnostics.Add(new MapPackageDiagnostic(
                        MapPackageDiagnostic.ChunkInventory,
                        $"Layer entry ({entry.ChunkX}, {entry.ChunkY}, imagery) lies outside the {layout.ChunksX} x {layout.ChunksY} chunk grid."));
                    continue;
                }

                if (!seenImagery.Add((entry.ChunkX, entry.ChunkY)))
                {
                    diagnostics.Add(new MapPackageDiagnostic(
                        MapPackageDiagnostic.ChunkInventory,
                        $"Duplicate imagery layer entry for chunk ({entry.ChunkX}, {entry.ChunkY})."));
                }
            }
        }
    }

    private static void ValidatePalette(MapPackageBlendModel? blend, List<MapPackageDiagnostic> diagnostics)
    {
        if (blend?.Palette is null)
        {
            return;
        }

        const int maxPaletteEntries = 256;

        if (blend.Palette.Count > maxPaletteEntries)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.PaletteCount,
                $"Palette holds {blend.Palette.Count} entries; at most {maxPaletteEntries} are allowed."));
            return;
        }

        var seen = new HashSet<byte>();

        foreach (var entry in blend.Palette)
        {
            if (entry is null)
            {
                RequireComponent(entry, "BlendModel.Palette entry", diagnostics);
                return;
            }

            if (seen.Add(entry.Index) && entry.Index < blend.Palette.Count)
            {
                continue;
            }

            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.PaletteIndex,
                $"Palette entry '{entry.Name}' declares index {entry.Index}; entry indices must be unique and below entry count {blend.Palette.Count}."));
            return;
        }
    }

    private static void ValidateQuantities(MapPackageManifest manifest, List<MapPackageDiagnostic> diagnostics)
    {
        if (manifest.Layout is not { } layout)
        {
            return;
        }

        RequireFinitePositiveQuantity(layout.SampleSpacing, "TerrainLayout.SampleSpacing", diagnostics);
        RequireUnitFrame(layout.SampleSpacing, "TerrainLayout.SampleSpacing", diagnostics);

        if (!string.Equals(layout.SampleSpacing.Frame, nameof(MapQuantityFrame.MapLocalXy), StringComparison.Ordinal))
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.QuantityUnitFrame,
                $"TerrainLayout.SampleSpacing frame is '{layout.SampleSpacing.Frame}'; expected '{nameof(MapQuantityFrame.MapLocalXy)}'."));
        }

        if (layout.GeospatialTransform is not { } transform)
        {
            return;
        }

        if (!double.IsFinite(transform.MetersPerGameUnit) || transform.MetersPerGameUnit <= 0)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.QuantityFinite,
                $"GeospatialTransform.MetersPerGameUnit is {transform.MetersPerGameUnit}; it must be finite and greater than zero."));
        }

        if (transform.VerticalExaggeration is { } exaggeration && !double.IsFinite(exaggeration))
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.QuantityFinite,
                $"GeospatialTransform.VerticalExaggeration is {exaggeration}; it must be finite when present."));
        }

        if (transform.OriginEasting is { } easting)
        {
            RequireQuantity(easting, "GeospatialTransform.OriginEasting", diagnostics);
        }

        if (transform.OriginNorthing is { } northing)
        {
            RequireQuantity(northing, "GeospatialTransform.OriginNorthing", diagnostics);
        }

        if (transform.VerticalOrigin is { } vertical)
        {
            RequireQuantity(vertical, "GeospatialTransform.VerticalOrigin", diagnostics);
        }
    }

    private static void ValidatePlacements(
        MapPackageManifest manifest,
        HashSet<string> stableIds,
        List<MapPackageDiagnostic> diagnostics)
    {
        if (manifest.Objects is null)
        {
            return;
        }

        foreach (var placed in manifest.Objects)
        {
            if (placed is null)
            {
                RequireComponent(placed, "PlacedObject", diagnostics);
                continue;
            }

            RequireUniqueStableId(stableIds, placed.StableId, diagnostics);
            RequireQuantity(placed.X, $"PlacedObject '{placed.StableId}' X", diagnostics);
            RequireQuantity(placed.Y, $"PlacedObject '{placed.StableId}' Y", diagnostics);
            RequireQuantity(placed.Z, $"PlacedObject '{placed.StableId}' Z", diagnostics);
        }
    }

    private static void ValidateWaypoints(
        MapPackageManifest manifest,
        HashSet<string> stableIds,
        List<MapPackageDiagnostic> diagnostics)
    {
        if (manifest.Waypoints is null)
        {
            return;
        }

        foreach (var waypoint in manifest.Waypoints)
        {
            if (waypoint is null)
            {
                RequireComponent(waypoint, "Waypoint", diagnostics);
                continue;
            }

            RequireUniqueStableId(stableIds, waypoint.StableId, diagnostics);
            RequireQuantity(waypoint.X, $"Waypoint '{waypoint.StableId}' X", diagnostics);
            RequireQuantity(waypoint.Y, $"Waypoint '{waypoint.StableId}' Y", diagnostics);
            RequireQuantity(waypoint.Z, $"Waypoint '{waypoint.StableId}' Z", diagnostics);
        }
    }

    private static void ValidateProvenance(
        MapPackageManifest manifest,
        HashSet<string> stableIds,
        List<MapPackageDiagnostic> diagnostics)
    {
        if (manifest.Provenance is not { } provenance)
        {
            return;
        }

        if (provenance.GeneratedFeatures is not null)
        {
            foreach (var feature in provenance.GeneratedFeatures)
            {
                if (feature is null)
                {
                    RequireComponent(feature, "GeneratedFeature", diagnostics);
                    continue;
                }

                RequireUniqueStableId(stableIds, feature.StableId, diagnostics);
            }
        }

        if (provenance.Sources is not null)
        {
            foreach (var source in provenance.Sources)
            {
                if (source is null)
                {
                    RequireComponent(source, "SourceRecord", diagnostics);
                    continue;
                }

                if (source.OriginX is { } originX)
                {
                    RequireQuantity(originX, $"SourceRecord '{source.Kind}' OriginX", diagnostics);
                }

                if (source.OriginY is { } originY)
                {
                    RequireQuantity(originY, $"SourceRecord '{source.Kind}' OriginY", diagnostics);
                }

                if (source.Sha256Digests is not null)
                {
                    for (var i = 0; i < source.Sha256Digests.Count; i++)
                    {
                        var digest = source.Sha256Digests[i];

                        if (!MapPackagePath.IsValidSha256(digest))
                        {
                            diagnostics.Add(new MapPackageDiagnostic(
                                MapPackageDiagnostic.DigestInvalid,
                                $"SourceRecord '{source.Kind}' digest[{i}] '{digest}' must be 64 lowercase hex characters."));
                        }
                    }
                }
            }
        }
    }

    private static void ValidateRetainedSections(MapPackageManifest manifest, List<MapPackageDiagnostic> diagnostics)
    {
        if (manifest.RetainedSections is null)
        {
            return;
        }

        var seenSectionNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var section in manifest.RetainedSections)
        {
            if (section is null)
            {
                RequireComponent(section, "RetainedSection", diagnostics);
                continue;
            }

            if (!seenSectionNames.Add(section.SectionName))
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.RetainedNameDuplicate,
                    $"Duplicate retained section name '{section.SectionName}'."));
            }

            if (!MapSectionNames.IsKnownSection(section.SectionName))
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.RetainedName,
                    $"Retained section '{section.SectionName}' is not a known legacy section name."));
            }

            if (section.SectionVersion < 0)
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.RetainedVersion,
                    $"Retained section '{section.SectionName}' carries version {section.SectionVersion}; section versions are non-negative."));
            }

            if (section.ByteLength <= 0)
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.RetainedLength,
                    $"Retained section '{section.SectionName}' declares non-positive byte length {section.ByteLength}."));
            }

            if (!MapPackagePath.TryValidatePath(section.Path, out var pathReason))
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.PathInvalid,
                    $"Retained section '{section.SectionName}' path is invalid: {pathReason}"));
            }

            if (!MapPackagePath.IsValidSha256(section.Sha256))
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.DigestInvalid,
                    $"Retained section '{section.SectionName}' SHA-256 '{section.Sha256}' must be 64 lowercase hex characters."));
            }
        }
    }

    private static void RequireUniqueStableId(HashSet<string> seen, string stableId, List<MapPackageDiagnostic> diagnostics)
    {
        if (seen.Add(stableId))
        {
            return;
        }

        diagnostics.Add(new MapPackageDiagnostic(
            MapPackageDiagnostic.StableIdDuplicate,
            $"StableId '{stableId}' is used by more than one placed object, waypoint, or generated feature."));
    }

    private static void RequireQuantity(MapPackageQuantity quantity, string field, List<MapPackageDiagnostic> diagnostics)
    {
        RequireFiniteQuantity(quantity, field, diagnostics);
        RequireUnitFrame(quantity, field, diagnostics);
    }

    private static void RequireUnitFrame(MapPackageQuantity quantity, string field, List<MapPackageDiagnostic> diagnostics)
    {
        var unitValid = Enum.TryParse<MapQuantityUnit>(quantity.Unit, false, out _);
        var frameValid = Enum.TryParse<MapQuantityFrame>(quantity.Frame, false, out _);

        if (unitValid && frameValid)
        {
            return;
        }

        diagnostics.Add(new MapPackageDiagnostic(
            MapPackageDiagnostic.QuantityUnitFrame,
            $"{field} declares unit '{quantity.Unit}' and frame '{quantity.Frame}'; unit and frame must match known MapQuantityUnit and MapQuantityFrame names."));
    }

    private static void RequireFiniteQuantity(MapPackageQuantity quantity, string field, List<MapPackageDiagnostic> diagnostics)
    {
        if (double.IsFinite(quantity.Value))
        {
            return;
        }

        diagnostics.Add(new MapPackageDiagnostic(
            MapPackageDiagnostic.QuantityFinite,
            $"{field} value is {quantity.Value}; quantity values must be finite."));
    }

    private static void RequireFinitePositiveQuantity(MapPackageQuantity quantity, string field, List<MapPackageDiagnostic> diagnostics)
    {
        if (double.IsFinite(quantity.Value) && quantity.Value > 0)
        {
            return;
        }

        diagnostics.Add(new MapPackageDiagnostic(
            MapPackageDiagnostic.QuantityFinite,
            $"{field} value is {quantity.Value}; it must be finite and greater than zero."));
    }
}
