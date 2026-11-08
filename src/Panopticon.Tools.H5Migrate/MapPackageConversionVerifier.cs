#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;

namespace Panopticon.Tools.H5Migrate;

/// <summary>Outcome of verifying one converted package: success plus named diagnostics when false.</summary>
public sealed record MapPackageConversionResult(bool Success, IReadOnlyList<MapPackageDiagnostic> Diagnostics);

/// <summary>
/// Conversion gates over a written package (WI-H5-01): the manifest accounts for every source
/// section, typed sections compare semantically equal (heights, spacing, objects, waypoints,
/// identity), and every retained section matches by version and SHA-256. Heights compare
/// bit-exact and mask, material, and blend bits compare byte-identical: conversion copies
/// values verbatim with no polarity inference and no normalization.
/// </summary>
public static class MapPackageConversionVerifier
{
    public static MapPackageConversionResult Verify(MapFile sourceMap, MapInterchange original, MapInterchange reloaded)
    {
        ArgumentNullException.ThrowIfNull(sourceMap);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(reloaded);

        var diagnostics = new List<MapPackageDiagnostic>();
        VerifySourceSectionsAccounted(sourceMap, reloaded, diagnostics);
        VerifyIdentityEqual(original, reloaded, diagnostics);
        VerifyLayoutEqual(original, reloaded, diagnostics);
        VerifyBlendEqual(original, reloaded, diagnostics);
        VerifyObjectsEqual(original, reloaded, diagnostics);
        VerifyWaypointsEqual(original, reloaded, diagnostics);
        VerifyRetainedSectionsMatch(original, reloaded, diagnostics);

        return new MapPackageConversionResult(diagnostics.Count == 0, diagnostics);
    }

    private static void VerifySourceSectionsAccounted(MapFile sourceMap, MapInterchange reloaded, List<MapPackageDiagnostic> diagnostics)
    {
        var retainedNames = reloaded.RetainedSections.Select(section => section.SectionName).ToHashSet(StringComparer.Ordinal);

        foreach (var section in MapSourceSectionInventory.Describe(sourceMap))
        {
            if (section.Disposition == MapSourceSectionDisposition.Retained && !retainedNames.Contains(section.Name))
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    H5MigrateCliDiagnostics.SourceSectionGap,
                    $"Source section '{section.Name}' has no disposition: it is neither a typed home nor a retained manifest entry."));
            }
        }
    }

    private static void VerifyIdentityEqual(MapInterchange original, MapInterchange reloaded, List<MapPackageDiagnostic> diagnostics)
    {
        if (!string.Equals(original.Identity.Name, reloaded.Identity.Name, StringComparison.Ordinal))
        {
            Mismatch(diagnostics, $"identity.name '{original.Identity.Name}' differs from re-read '{reloaded.Identity.Name}'.");
        }

        if (original.Identity.LegacyGlobalVersion != reloaded.Identity.LegacyGlobalVersion)
        {
            Mismatch(diagnostics, $"identity.legacyGlobalVersion {original.Identity.LegacyGlobalVersion} differs from re-read {reloaded.Identity.LegacyGlobalVersion}.");
        }

        if (reloaded.Identity.SourceKind != MapSourceKind.H3Package)
        {
            Mismatch(diagnostics, $"identity.sourceKind re-read as '{reloaded.Identity.SourceKind}'; a package read must declare H3Package.");
        }
    }

    private static void VerifyLayoutEqual(MapInterchange original, MapInterchange reloaded, List<MapPackageDiagnostic> diagnostics)
    {
        if (original.SchemaVersion != reloaded.SchemaVersion)
        {
            Mismatch(diagnostics, $"schemaVersion {original.SchemaVersion} differs from re-read {reloaded.SchemaVersion}.");
        }

        var originalLayout = original.Layout;
        var reloadedLayout = reloaded.Layout;

        if (originalLayout.LatticeWidth != reloadedLayout.LatticeWidth || originalLayout.LatticeHeight != reloadedLayout.LatticeHeight)
        {
            Mismatch(diagnostics, $"lattice {originalLayout.LatticeWidth}x{originalLayout.LatticeHeight} differs from re-read {reloadedLayout.LatticeWidth}x{reloadedLayout.LatticeHeight}.");
        }

        if (originalLayout.ChunksX != reloadedLayout.ChunksX || originalLayout.ChunksY != reloadedLayout.ChunksY)
        {
            Mismatch(diagnostics, $"chunk grid {originalLayout.ChunksX}x{originalLayout.ChunksY} differs from re-read {reloadedLayout.ChunksX}x{reloadedLayout.ChunksY}.");
        }

        if (originalLayout.Border != reloadedLayout.Border)
        {
            Mismatch(diagnostics, $"border {originalLayout.Border} differs from re-read {reloadedLayout.Border}.");
        }

        if (originalLayout.NodataPolicy != reloadedLayout.NodataPolicy)
        {
            Mismatch(diagnostics, $"nodataPolicy {originalLayout.NodataPolicy} differs from re-read {reloadedLayout.NodataPolicy}.");
        }

        if (originalLayout.HeightSectionVersion != reloadedLayout.HeightSectionVersion)
        {
            Mismatch(diagnostics, $"heightSectionVersion {originalLayout.HeightSectionVersion} differs from re-read {reloadedLayout.HeightSectionVersion}.");
        }

        if (originalLayout.MapArea != reloadedLayout.MapArea)
        {
            Mismatch(diagnostics, $"mapArea {originalLayout.MapArea} differs from re-read {reloadedLayout.MapArea}.");
        }

        VerifyQuantityEqual(originalLayout.SampleSpacing, reloadedLayout.SampleSpacing, "layout.sampleSpacing", diagnostics);

        if (originalLayout.TerrainBorders.Count != reloadedLayout.TerrainBorders.Count)
        {
            Mismatch(diagnostics, $"terrainBorders count {originalLayout.TerrainBorders.Count} differs from re-read {reloadedLayout.TerrainBorders.Count}.");
        }
        else
        {
            for (var index = 0; index < originalLayout.TerrainBorders.Count; index++)
            {
                if (!originalLayout.TerrainBorders[index].Equals(reloadedLayout.TerrainBorders[index]))
                {
                    Mismatch(diagnostics, $"terrainBorders[{index}] differs from the re-read border.");
                }
            }
        }

        VerifyChunksEqual(originalLayout.Chunks, reloadedLayout.Chunks, diagnostics);
    }

    private static void VerifyChunksEqual(IReadOnlyList<Chunk> originalChunks, IReadOnlyList<Chunk> reloadedChunks, List<MapPackageDiagnostic> diagnostics)
    {
        if (originalChunks.Count != reloadedChunks.Count)
        {
            Mismatch(diagnostics, $"chunk count {originalChunks.Count} differs from re-read {reloadedChunks.Count}.");
            return;
        }

        for (var index = 0; index < originalChunks.Count; index++)
        {
            var originalChunk = originalChunks[index];
            var reloadedChunk = reloadedChunks[index];

            if (originalChunk.Key != reloadedChunk.Key)
            {
                Mismatch(diagnostics, $"chunk [{index}] key {originalChunk.Key} differs from re-read {reloadedChunk.Key}.");
                continue;
            }

            VerifyHeightsBitExact(originalChunk, reloadedChunk, index, diagnostics);
            VerifyLayerBytes(originalChunk.Materials, reloadedChunk.Materials, layer => layer.Indices, $"chunk ({originalChunk.Key.Cx}, {originalChunk.Key.Cy}) materials", diagnostics);
            VerifyLayerBytes(originalChunk.Masks, reloadedChunk.Masks, layer => layer.Flags, $"chunk ({originalChunk.Key.Cx}, {originalChunk.Key.Cy}) masks", diagnostics);
            VerifyImageryEqual(originalChunk, reloadedChunk, diagnostics);
        }
    }

    private static void VerifyHeightsBitExact(Chunk originalChunk, Chunk reloadedChunk, int index, List<MapPackageDiagnostic> diagnostics)
    {
        var originalSamples = originalChunk.Heights.Samples;
        var reloadedSamples = reloadedChunk.Heights.Samples;

        if (originalSamples.Count != reloadedSamples.Count)
        {
            Mismatch(diagnostics, $"chunk [{index}] heights sample count {originalSamples.Count} differs from re-read {reloadedSamples.Count}.");
            return;
        }

        for (var sample = 0; sample < originalSamples.Count; sample++)
        {
            var originalBits = BitConverter.SingleToUInt32Bits(originalSamples[sample]);
            var reloadedBits = BitConverter.SingleToUInt32Bits(reloadedSamples[sample]);

            if (originalBits != reloadedBits)
            {
                Mismatch(diagnostics, $"chunk [{index}] height sample {sample} is not bit-exact: 0x{originalBits:X8} vs re-read 0x{reloadedBits:X8}.");
                return;
            }
        }
    }

    private static void VerifyLayerBytes<TLayer>(
        TLayer? originalLayer,
        TLayer? reloadedLayer,
        Func<TLayer, IReadOnlyList<byte>?> bytes,
        string label,
        List<MapPackageDiagnostic> diagnostics)
        where TLayer : class
    {
        if (originalLayer is null || reloadedLayer is null)
        {
            if ((originalLayer is null) != (reloadedLayer is null))
            {
                Mismatch(diagnostics, $"{label} presence differs from the re-read package.");
            }

            return;
        }

        var originalBytes = bytes(originalLayer);
        var reloadedBytes = bytes(reloadedLayer);

        if (originalBytes is null || reloadedBytes is null)
        {
            Mismatch(diagnostics, $"{label} payload is missing on one side.");
            return;
        }

        if (!originalBytes.SequenceEqual(reloadedBytes))
        {
            Mismatch(diagnostics, $"{label} bytes differ from the re-read package; conversion must copy them verbatim.");
        }
    }

    private static void VerifyImageryEqual(Chunk originalChunk, Chunk reloadedChunk, List<MapPackageDiagnostic> diagnostics)
    {
        if (originalChunk.Imagery is null || reloadedChunk.Imagery is null)
        {
            if ((originalChunk.Imagery is null) != (reloadedChunk.Imagery is null))
            {
                Mismatch(diagnostics, $"chunk ({originalChunk.Key.Cx}, {originalChunk.Key.Cy}) imagery presence differs from the re-read package.");
            }

            return;
        }

        var original = originalChunk.Imagery;
        var reloaded = reloadedChunk.Imagery;

        if (original.Width != reloaded.Width || original.Height != reloaded.Height || !original.Rgba.SequenceEqual(reloaded.Rgba))
        {
            Mismatch(diagnostics, $"chunk ({originalChunk.Key.Cx}, {originalChunk.Key.Cy}) imagery bytes differ from the re-read package.");
        }
    }

    private static void VerifyBlendEqual(MapInterchange original, MapInterchange reloaded, List<MapPackageDiagnostic> diagnostics)
    {
        var originalPalette = original.Blend.Palette;
        var reloadedPalette = reloaded.Blend.Palette;

        if (originalPalette.Count != reloadedPalette.Count)
        {
            Mismatch(diagnostics, $"blend palette count {originalPalette.Count} differs from re-read {reloadedPalette.Count}.");
            return;
        }

        for (var index = 0; index < originalPalette.Count; index++)
        {
            if (!originalPalette[index].Equals(reloadedPalette[index]))
            {
                Mismatch(diagnostics, $"blend palette entry [{index}] differs from the re-read package.");
            }
        }
    }

    private static void VerifyObjectsEqual(MapInterchange original, MapInterchange reloaded, List<MapPackageDiagnostic> diagnostics)
    {
        if (original.Objects is null || reloaded.Objects is null)
        {
            if ((original.Objects is null) != (reloaded.Objects is null))
            {
                Mismatch(diagnostics, "objects presence differs from the re-read package.");
            }

            return;
        }

        var originalObjects = original.Objects.Objects;
        var reloadedObjects = reloaded.Objects.Objects;

        if (originalObjects.Count != reloadedObjects.Count)
        {
            Mismatch(diagnostics, $"objects count {originalObjects.Count} differs from re-read {reloadedObjects.Count}.");
            return;
        }

        for (var index = 0; index < originalObjects.Count; index++)
        {
            var originalObject = originalObjects[index];
            var reloadedObject = reloadedObjects[index];

            if (!string.Equals(originalObject.StableId, reloadedObject.StableId, StringComparison.Ordinal))
            {
                Mismatch(diagnostics, $"object [{index}] stableId '{originalObject.StableId}' differs from re-read '{reloadedObject.StableId}'.");
            }

            VerifyQuantityEqual(originalObject.X, reloadedObject.X, $"object '{originalObject.StableId}' x", diagnostics);
            VerifyQuantityEqual(originalObject.Y, reloadedObject.Y, $"object '{originalObject.StableId}' y", diagnostics);
            VerifyQuantityEqual(originalObject.Z, reloadedObject.Z, $"object '{originalObject.StableId}' z", diagnostics);

            if (originalObject.OrientationRadians != reloadedObject.OrientationRadians)
            {
                Mismatch(diagnostics, $"object '{originalObject.StableId}' orientationRadians {originalObject.OrientationRadians} differs from re-read {reloadedObject.OrientationRadians}.");
            }

            if (!originalObject.Type.Equals(reloadedObject.Type))
            {
                Mismatch(diagnostics, $"object '{originalObject.StableId}' type differs from the re-read package.");
            }

            if (originalObject.RoadType != reloadedObject.RoadType)
            {
                Mismatch(diagnostics, $"object '{originalObject.StableId}' roadType {originalObject.RoadType} differs from re-read {reloadedObject.RoadType}.");
            }

            if (originalObject.LegacyProperties.Count != reloadedObject.LegacyProperties.Count)
            {
                Mismatch(diagnostics, $"object '{originalObject.StableId}' legacyProperties count {originalObject.LegacyProperties.Count} differs from re-read {reloadedObject.LegacyProperties.Count}.");
            }
            else
            {
                foreach (var (key, value) in originalObject.LegacyProperties)
                {
                    if (!reloadedObject.LegacyProperties.TryGetValue(key, out var reloadedValue) || !value.Equals(reloadedValue))
                    {
                        Mismatch(diagnostics, $"object '{originalObject.StableId}' property '{key}' differs from the re-read package.");
                    }
                }
            }
        }
    }

    private static void VerifyWaypointsEqual(MapInterchange original, MapInterchange reloaded, List<MapPackageDiagnostic> diagnostics)
    {
        if (original.Waypoints is null || reloaded.Waypoints is null)
        {
            if ((original.Waypoints is null) != (reloaded.Waypoints is null))
            {
                Mismatch(diagnostics, "waypoints presence differs from the re-read package.");
            }

            return;
        }

        var originalWaypoints = original.Waypoints.Waypoints;
        var reloadedWaypoints = reloaded.Waypoints.Waypoints;

        if (originalWaypoints.Count != reloadedWaypoints.Count)
        {
            Mismatch(diagnostics, $"waypoints count {originalWaypoints.Count} differs from re-read {reloadedWaypoints.Count}.");
            return;
        }

        for (var index = 0; index < originalWaypoints.Count; index++)
        {
            var originalWaypoint = originalWaypoints[index];
            var reloadedWaypoint = reloadedWaypoints[index];

            if (!string.Equals(originalWaypoint.StableId, reloadedWaypoint.StableId, StringComparison.Ordinal) ||
                !string.Equals(originalWaypoint.Name, reloadedWaypoint.Name, StringComparison.Ordinal))
            {
                Mismatch(diagnostics, $"waypoint [{index}] identity differs from the re-read package.");
            }

            VerifyQuantityEqual(originalWaypoint.X, reloadedWaypoint.X, $"waypoint '{originalWaypoint.StableId}' x", diagnostics);
            VerifyQuantityEqual(originalWaypoint.Y, reloadedWaypoint.Y, $"waypoint '{originalWaypoint.StableId}' y", diagnostics);
            VerifyQuantityEqual(originalWaypoint.Z, reloadedWaypoint.Z, $"waypoint '{originalWaypoint.StableId}' z", diagnostics);
        }
    }

    private static void VerifyRetainedSectionsMatch(MapInterchange original, MapInterchange reloaded, List<MapPackageDiagnostic> diagnostics)
    {
        if (original.RetainedSections.Count != reloaded.RetainedSections.Count)
        {
            Mismatch(diagnostics, $"retained section count {original.RetainedSections.Count} differs from re-read {reloaded.RetainedSections.Count}.");
            return;
        }

        for (var index = 0; index < original.RetainedSections.Count; index++)
        {
            var originalSection = original.RetainedSections[index];
            var reloadedSection = reloaded.RetainedSections[index];

            if (!string.Equals(originalSection.SectionName, reloadedSection.SectionName, StringComparison.Ordinal))
            {
                Mismatch(diagnostics, $"retained section [{index}] name '{originalSection.SectionName}' differs from re-read '{reloadedSection.SectionName}'.");
                continue;
            }

            if (originalSection.SectionVersion != reloadedSection.SectionVersion)
            {
                Mismatch(diagnostics, $"retained section '{originalSection.SectionName}' version {originalSection.SectionVersion} differs from re-read {reloadedSection.SectionVersion}.");
            }

            if (!string.Equals(originalSection.Sha256, reloadedSection.Sha256, StringComparison.Ordinal))
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    H5MigrateCliDiagnostics.RetainedDigestMismatch,
                    $"Retained section '{originalSection.SectionName}' SHA-256 differs from the re-read package: {originalSection.Sha256} vs {reloadedSection.Sha256}."));
            }

            if (!originalSection.Bytes.Span.SequenceEqual(reloadedSection.Bytes.Span))
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    H5MigrateCliDiagnostics.RetainedDigestMismatch,
                    $"Retained section '{originalSection.SectionName}' bytes differ from the re-read package despite matching digests."));
            }
        }
    }

    private static void VerifyQuantityEqual(MapQuantity original, MapQuantity reloaded, string label, List<MapPackageDiagnostic> diagnostics)
    {
        if (!original.Equals(reloaded))
        {
            Mismatch(diagnostics, $"{label} {original.Value} ({original.Unit}, {original.Frame}) differs from re-read {reloaded.Value} ({reloaded.Unit}, {reloaded.Frame}).");
        }
    }

    private static void Mismatch(List<MapPackageDiagnostic> diagnostics, string message)
    {
        diagnostics.Add(new MapPackageDiagnostic(H5MigrateCliDiagnostics.TypedMismatch, message));
    }
}
