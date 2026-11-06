#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing.Maps;
using Panopticon.Data.Map;

namespace Panopticon.Content.Importing.MapSources;

/// <summary>
/// Runtime bridge from a validated <see cref="MapInterchange"/> to resident simulation state:
/// <see cref="HeightMapData"/>, <see cref="MapObject"/>[], and the navigation impassability feed.
/// Every public method validates first and throws <see cref="MapImportException"/> carrying every
/// finding; results build fully into locals and return only on success, so a rejected model
/// publishes no partial residents.
/// </summary>
public static class RuntimeBridge
{
    private const int IntervalsPerChunkAxis = MapChunkGeometry.IntervalsPerChunkAxis;

    private const int CorePointsPerAxis = MapChunkGeometry.CorePointsPerAxis;

    public static HeightMapData MaterializeHeightMapData(MapInterchange model)
    {
        ValidateOrThrow(model);

        var layout = model.Layout;
        var latticeWidth = layout.LatticeWidth;
        var latticeHeight = layout.LatticeHeight;

        // Mirrors the HeightMapData.Parse Area guard: the legacy HeightMapData section stores the
        // whole-map lattice point count, and a mismatch means the chunk lattice cannot rebuild it.
        // Compared as ulong so a wrapped int product can never false-match.
        if (layout.MapArea != (ulong)((long)latticeWidth * latticeHeight))
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.HeightRepresentable,
                    $"TerrainLayout.MapArea is {layout.MapArea}; the {latticeWidth} x {latticeHeight} lattice holds {(long)latticeWidth * latticeHeight} points."),
            ]);
        }

        // PartitionCellManager.OnNewGame reads Borders[0] as the terrain boundary.
        if (layout.TerrainBorders.Count == 0)
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.HeightRepresentable,
                    "TerrainLayout carries no terrain borders; PartitionCellManager needs Borders[0]."),
            ]);
        }

        // FromMaterialized stores the border as uint and the section version as ushort; a value
        // outside those ranges would wrap silently, so it is rejected before any materialization.
        if (layout.Border < 0)
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.HeightRepresentable,
                    $"TerrainLayout.Border is {layout.Border}; HeightMapData stores the border as an unsigned 32-bit value."),
            ]);
        }

        if (layout.HeightSectionVersion < 0 || layout.HeightSectionVersion > 65535)
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.HeightRepresentable,
                    $"TerrainLayout.HeightSectionVersion is {layout.HeightSectionVersion}; HeightMapData stores the section version as an unsigned 16-bit value."),
            ]);
        }

        // Exactness fact: 0.0390625 is 5/128 (not 1/32) and 0.625 is 5/8. Max elevation 65535
        // times numerator 5 is 327675 < 2^24, and the divisions by 8 and 128 are exact, so
        // ushort * scale is exact in float32 and legacy-derived samples invert to their exact
        // quantized elevation; genuinely nonrepresentable H3/synthetic values fail honestly below.
        // The expression must stay identical to HeightMapData.VerticalScale.
        var scale = layout.HeightSectionVersion >= 5 ? 0.0390625f : 0.625f;
        var maxElevation = layout.HeightSectionVersion >= 5 ? 65535 : 255;

        var diagnostics = new List<MapInterchangeDiagnostic>();
        var elevations = new ushort[latticeWidth, latticeHeight];

        foreach (var chunk in layout.Chunks)
        {
            MaterializeChunkHeights(chunk, scale, maxElevation, elevations, diagnostics);
        }

        if (diagnostics.Count > 0)
        {
            throw new MapImportException(diagnostics);
        }

        var borders = new HeightMapBorder[layout.TerrainBorders.Count];

        for (var i = 0; i < borders.Length; i++)
        {
            var border = layout.TerrainBorders[i];
            borders[i] = new HeightMapBorder
            {
                Corner1X = border.Corner1X,
                Corner1Y = border.Corner1Y,
                X = border.X,
                Y = border.Y,
            };
        }

        return HeightMapData.FromMaterialized((uint)layout.Border, elevations, borders, (ushort)layout.HeightSectionVersion);
    }

    public static MapObject[] MaterializeMapObjects(MapInterchange model)
    {
        ValidateOrThrow(model);

        // The placed-object set is optional in the interchange; no set publishes no objects.
        if (model.Objects is null)
        {
            return [];
        }

        var placed = model.Objects.Objects;
        var diagnostics = new List<MapInterchangeDiagnostic>();
        var objects = new MapObject[placed.Count];

        for (var index = 0; index < placed.Count; index++)
        {
            var source = placed[index];
            var subject = $"PlacedObject '{source.StableId}'";

            // TypeName accepts any AssetReference scope: scope resolution happens downstream of
            // this bridge, so the scope is not checked here.
            var mapObject = new MapObject(
                new Vector3(
                    ToRepresentableFloat(source.X.Value, $"{subject} X", diagnostics),
                    ToRepresentableFloat(source.Y.Value, $"{subject} Y", diagnostics),
                    ToRepresentableFloat(source.Z.Value, $"{subject} Z", diagnostics)),
                ToRepresentableFloat(source.OrientationRadians, $"{subject} Angle", diagnostics),
                (RoadType)source.RoadType,
                source.Type.Name);

            foreach (var (name, value) in source.LegacyProperties)
            {
                switch (value)
                {
                    case MapPropertyValue.BooleanValue boolean:
                        mapObject.Properties.AddBoolean(name, boolean.Value);
                        break;
                    case MapPropertyValue.IntegerValue integer:
                        mapObject.Properties.AddInteger(name, integer.Value);
                        break;
                    case MapPropertyValue.RealValue real:
                        mapObject.Properties.AddReal(name, real.Value);
                        break;
                    case MapPropertyValue.AsciiStringValue ascii:
                        mapObject.Properties.AddAsciiString(name, ascii.Value);
                        break;
                    case MapPropertyValue.UnicodeStringValue unicode:
                        mapObject.Properties.AddUnicodeString(name, unicode.Value);
                        break;
                    default:
                        // The MapPropertyValue union is closed over the five representable kinds;
                        // this arm only fails honestly if that invariant is ever broken.
                        throw new MapImportException(
                        [
                            new MapInterchangeDiagnostic(
                                MapInterchangeDiagnostic.PropertyType,
                                $"{subject} carries property '{name}' of unsupported kind {value.GetType().Name}."),
                        ]);
                }
            }

            objects[index] = mapObject;
        }

        if (diagnostics.Count > 0)
        {
            throw new MapImportException(diagnostics);
        }

        return objects;
    }

    /// <summary>
    /// Builds the navigation impassability feed from chunk core mask bits. Returns false with a
    /// null feed when any chunk lacks masks (heights-only legacy sources); the caller then falls
    /// back to BlendTileData. The Unbuildable bit is carried in the model but deliberately not
    /// applied to any resident feed here.
    /// </summary>
    public static bool TryGetMaskFeed(MapInterchange model, out bool[,] impassable)
    {
        ValidateOrThrow(model);

        var layout = model.Layout;

        // Layers-consistency requires masks on every chunk or none; the per-chunk check keeps the
        // false path honest regardless.
        if (layout.Chunks.Any(chunk => chunk.Masks is null))
        {
            impassable = null!;
            return false;
        }

        var latticeWidth = layout.LatticeWidth;
        var latticeHeight = layout.LatticeHeight;
        var feed = new bool[latticeWidth, latticeHeight];

        foreach (var chunk in layout.Chunks)
        {
            var flags = chunk.Masks!.Flags;
            var coreWidth = CoreExtent(latticeWidth, chunk.Key.Cx);
            var coreHeight = CoreExtent(latticeHeight, chunk.Key.Cy);
            var originX = chunk.Key.Cx * IntervalsPerChunkAxis;
            var originY = chunk.Key.Cy * IntervalsPerChunkAxis;

            for (var j = 0; j < coreHeight; j++)
            {
                for (var i = 0; i < coreWidth; i++)
                {
                    feed[originX + i, originY + j] = (flags[(j * coreWidth) + i] & (byte)MapMaskFlags.Impassable) != 0;
                }
            }
        }

        impassable = feed;
        return true;
    }

    private static void ValidateOrThrow(MapInterchange model)
    {
        var diagnostics = MapInterchangeValidator.Validate(model);

        if (diagnostics.Count > 0)
        {
            throw new MapImportException(diagnostics);
        }
    }

    private static void MaterializeChunkHeights(Chunk chunk, float scale, int maxElevation, ushort[,] elevations, List<MapInterchangeDiagnostic> diagnostics)
    {
        var latticeWidth = elevations.GetLength(0);
        var latticeHeight = elevations.GetLength(1);
        var coreWidth = CoreExtent(latticeWidth, chunk.Key.Cx);
        var coreHeight = CoreExtent(latticeHeight, chunk.Key.Cy);
        var storedWidth = coreWidth + (2 * MapChunkGeometry.ApronSamplesPerSide);
        var originX = chunk.Key.Cx * IntervalsPerChunkAxis;
        var originY = chunk.Key.Cy * IntervalsPerChunkAxis;
        var samples = chunk.Heights.Samples;

        // Aprons are skipped: validation proved each shared seam holds one value and each apron
        // sample equals its neighbour's core sample, so core points alone rebuild the lattice.
        // Core (i, j) maps to stored row j + 1, column i + 1 (row 0 is the southern apron).
        for (var j = 0; j < coreHeight; j++)
        {
            for (var i = 0; i < coreWidth; i++)
            {
                var sample = samples[((j + 1) * storedWidth) + i + 1];

                if (RejectUnrepresentableHeight(chunk.Key, i, j, sample, scale, maxElevation, diagnostics))
                {
                    // Bounded like the validator: one height finding per chunk.
                    return;
                }

                elevations[originX + i, originY + j] = (ushort)MathF.Round(sample / scale);
            }
        }
    }

    private static bool RejectUnrepresentableHeight(ChunkKey key, int i, int j, float sample, float scale, int maxElevation, List<MapInterchangeDiagnostic> diagnostics)
    {
        if (!float.IsFinite(sample))
        {
            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.HeightRepresentable,
                $"Chunk {Describe(key)} core ({i}, {j}) height {sample} is not a finite float32 value."));
            return true;
        }

        var quantized = MathF.Round(sample / scale);

        if (quantized < 0 || quantized > maxElevation)
        {
            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.HeightRepresentable,
                $"Chunk {Describe(key)} core ({i}, {j}) height {sample} quantizes to {quantized}, outside the 0..{maxElevation} elevation range."));
            return true;
        }

        // No silent quantization: a sample whose nearest elevation does not reproduce it exactly
        // is nonrepresentable and fails here.
        if ((float)quantized * scale != sample)
        {
            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.HeightRepresentable,
                $"Chunk {Describe(key)} core ({i}, {j}) height {sample} has no exact elevation ((float){quantized} * {scale} != {sample})."));
            return true;
        }

        return false;
    }

    private static float ToRepresentableFloat(double value, string subject, List<MapInterchangeDiagnostic> diagnostics)
    {
        // Legacy coordinates and angles were float32 at rest; a double that does not round-trip
        // through float32 would silently shift the resident value.
        if ((double)(float)value != value)
        {
            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.ObjectRepresentable,
                $"{subject} is {value}; it does not survive the float32 position storage exactly."));
            return 0f;
        }

        return (float)value;
    }

    private static int CoreExtent(int latticePoints, int chunkIndex) => Math.Min(CorePointsPerAxis, latticePoints - (chunkIndex * IntervalsPerChunkAxis));

    private static string Describe(ChunkKey key) => $"{key.Cx}_{key.Cy}";
}
