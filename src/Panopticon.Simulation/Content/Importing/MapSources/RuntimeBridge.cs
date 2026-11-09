#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data.Map;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;
using Panopticon.Logic.Map;
using Panopticon.Terrain;

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

        var spacingMeters = layout.SampleSpacing.Unit == MapQuantityUnit.Meters
            ? layout.SampleSpacing.Value
            : UnitConversionPolicy.ScaleLength(layout.SampleSpacing.Value);

        if (!double.IsFinite(spacingMeters) || Math.Abs(spacingMeters - HeightMap.HorizontalScale) > 1e-4)
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.HeightRepresentable,
                    $"TerrainLayout.SampleSpacing converts to {spacingMeters} m; the runtime terrain spacing is {HeightMap.HorizontalScale} m."),
            ]);
        }

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
        // The expression must stay identical to HeightMapData.LegacyVerticalScale.
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

            var rawX = ToRepresentableFloat(source.X.Value, $"{subject} X", diagnostics);
            var rawY = ToRepresentableFloat(source.Y.Value, $"{subject} Y", diagnostics);
            var rawZ = ToRepresentableFloat(source.Z.Value, $"{subject} Z", diagnostics);

            var posX = source.X.Unit == MapQuantityUnit.Meters ? rawX : rawX * (float)UnitConversionPolicy.GeneralsFeetToMeters;
            var posY = source.Y.Unit == MapQuantityUnit.Meters ? rawY : rawY * (float)UnitConversionPolicy.GeneralsFeetToMeters;
            var posZ = source.Z.Unit == MapQuantityUnit.Meters ? rawZ : rawZ * (float)UnitConversionPolicy.GeneralsFeetToMeters;

            // TypeName accepts any AssetReference scope: scope resolution happens downstream of
            // this bridge, so the scope is not checked here.
            var mapObject = new MapObject(
                new Vector3(posX, posY, posZ),
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

    public static BlendTileData? MaterializeBlendTileData(MapInterchange model, HeightMapData? heightMapData = null)
    {
        ValidateOrThrow(model);

        if (model.BlendTileData is { } blend)
        {
            var heights = heightMapData ?? MaterializeHeightMapData(model);
            var bytes = SerializeBlendTileData(blend);
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            var context = new MapParseContext(new AssetNameCollection());
            return BlendTileData.Parse(reader, context, heights);
        }

        var retained = model.RetainedSections.FirstOrDefault(s => s.SectionName == BlendTileData.AssetName);
        if (retained is not null)
        {
            var heights = heightMapData ?? MaterializeHeightMapData(model);
            return MapBackendReaders.ReadRetainedBlendTileData(retained, heights);
        }

        return null;
    }

    public static GlobalLighting? MaterializeGlobalLighting(MapInterchange model)
    {
        ValidateOrThrow(model);

        if (model.GlobalLighting is { } lighting)
        {
            var bytes = SerializeGlobalLighting(lighting);
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            var context = new MapParseContext(new AssetNameCollection());
            return GlobalLighting.Parse(reader, context);
        }

        var retained = model.RetainedSections.FirstOrDefault(s => s.SectionName == GlobalLighting.AssetName);
        if (retained is not null)
        {
            return MapBackendReaders.ReadRetainedGlobalLighting(retained);
        }

        return null;
    }

    public static NamedCameras? MaterializeNamedCameras(MapInterchange model)
    {
        ValidateOrThrow(model);

        if (model.NamedCameras is { } cameras)
        {
            var bytes = SerializeNamedCameras(cameras);
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            var context = new MapParseContext(new AssetNameCollection());
            return NamedCameras.Parse(reader, context);
        }

        var retained = model.RetainedSections.FirstOrDefault(s => s.SectionName == NamedCameras.AssetName);
        if (retained is not null)
        {
            return MapBackendReaders.ReadRetainedNamedCameras(retained);
        }

        return null;
    }

    public static StandingWaterAreas? MaterializeStandingWaterAreas(MapInterchange model)
    {
        ValidateOrThrow(model);

        if (model.StandingWaterAreas is { } waterAreas)
        {
            var bytes = SerializeStandingWaterAreas(waterAreas);
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            var context = new MapParseContext(new AssetNameCollection());
            return StandingWaterAreas.Parse(reader, context);
        }

        var retained = model.RetainedSections.FirstOrDefault(s => s.SectionName == StandingWaterAreas.AssetName);
        if (retained is not null)
        {
            return MapBackendReaders.ReadRetainedStandingWaterAreas(retained);
        }

        return null;
    }

    public static StandingWaveAreas? MaterializeStandingWaveAreas(MapInterchange model)
    {
        ValidateOrThrow(model);

        if (model.StandingWaveAreas is { } waveAreas)
        {
            var bytes = SerializeStandingWaveAreas(waveAreas);
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            var context = new MapParseContext(new AssetNameCollection());
            return StandingWaveAreas.Parse(reader, context);
        }

        var retained = model.RetainedSections.FirstOrDefault(s => s.SectionName == StandingWaveAreas.AssetName);
        if (retained is not null)
        {
            return MapBackendReaders.ReadRetainedStandingWaveAreas(retained);
        }

        return null;
    }

    public static RiverAreas? MaterializeRiverAreas(MapInterchange model)
    {
        ValidateOrThrow(model);

        if (model.RiverAreas is { } riverAreas)
        {
            var bytes = SerializeRiverAreas(riverAreas);
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            var context = new MapParseContext(new AssetNameCollection());
            return RiverAreas.Parse(reader, context);
        }

        var retained = model.RetainedSections.FirstOrDefault(s => s.SectionName == RiverAreas.AssetName);
        if (retained is not null)
        {
            return MapBackendReaders.ReadRetainedRiverAreas(retained);
        }

        return null;
    }

    public static GlobalWaterSettings? MaterializeGlobalWaterSettings(MapInterchange model)
    {
        ValidateOrThrow(model);

        if (model.GlobalWaterSettings is { } waterSettings)
        {
            var bytes = SerializeGlobalWaterSettings(waterSettings);
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            var context = new MapParseContext(new AssetNameCollection());
            return GlobalWaterSettings.Parse(reader, context);
        }

        var retained = model.RetainedSections.FirstOrDefault(s => s.SectionName == GlobalWaterSettings.AssetName);
        if (retained is not null)
        {
            return MapBackendReaders.ReadRetainedGlobalWaterSettings(retained);
        }

        return null;
    }

    public static PolygonTriggers? MaterializePolygonTriggers(MapInterchange model)
    {
        ValidateOrThrow(model);

        if (model.PolygonTriggers is { } triggers)
        {
            var bytes = SerializePolygonTriggers(triggers);
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            var context = new MapParseContext(new AssetNameCollection());
            return PolygonTriggers.Parse(reader, context);
        }

        var retained = model.RetainedSections.FirstOrDefault(s => s.SectionName == PolygonTriggers.AssetName);
        if (retained is not null)
        {
            return MapBackendReaders.ReadRetainedPolygonTriggers(retained);
        }

        return null;
    }

    public static WaypointsList? MaterializeWaypointsList(MapInterchange model)
    {
        ValidateOrThrow(model);

        if (model.WaypointPaths is { } waypoints)
        {
            var bytes = SerializeWaypointsList(waypoints);
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            var context = new MapParseContext(new AssetNameCollection());
            return WaypointsList.Parse(reader, context);
        }

        var retained = model.RetainedSections.FirstOrDefault(s => s.SectionName == WaypointsList.AssetName);
        if (retained is not null)
        {
            return MapBackendReaders.ReadRetainedWaypointsList(retained);
        }

        return null;
    }

    private static byte[] SerializeBlendTileData(MapBlendTileData data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(data.Version);
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.Write(data.NumTiles);
        writer.WriteUInt16Array2D(data.Tiles);

        var blendBitSize = data.Version >= 14 && data.Version < 24 ? 32u : 16u;
        writer.WriteUIntArray2D(data.Blends, blendBitSize);
        writer.WriteUIntArray2D(data.ThreeWayBlends, blendBitSize);
        writer.WriteUIntArray2D(data.CliffTextures, blendBitSize);

        if (data.Version > 6)
        {
            writer.WriteSingleBitBooleanArray2D(data.Impassability);
        }

        if (data.Version >= 10 && data.ImpassabilityToPlayers is not null)
        {
            writer.WriteSingleBitBooleanArray2D(data.ImpassabilityToPlayers);
        }

        if (data.Version >= 11 && data.PassageWidths is not null)
        {
            writer.WriteSingleBitBooleanArray2D(data.PassageWidths);
        }

        if (data.Version >= 14 && data.Version < 25 && data.Taintability is not null)
        {
            writer.WriteSingleBitBooleanArray2D(data.Taintability);
        }

        if (data.Version >= 15 && data.ExtraPassability is not null)
        {
            writer.WriteSingleBitBooleanArray2D(data.ExtraPassability);
        }

        if (data.Version >= 16 && data.Version < 25 && data.Flammability is not null)
        {
            writer.WriteByteArray2D(data.Flammability);
        }

        if (data.Version >= 17 && data.Visibility is not null)
        {
            writer.WriteSingleBitBooleanArray2D(data.Visibility, padValue: 0xFF);
        }

        if (data.Version >= 24)
        {
            if (data.Buildability is not null)
            {
                writer.WriteSingleBitBooleanArray2D(data.Buildability);
            }

            if (data.ImpassabilityToAirUnits is not null)
            {
                writer.WriteSingleBitBooleanArray2D(data.ImpassabilityToAirUnits);
            }

            if (data.TiberiumGrowability is not null)
            {
                writer.WriteSingleBitBooleanArray2D(data.TiberiumGrowability);
            }
        }

        if (data.Version >= 25 && data.DynamicShrubberyDensity is not null)
        {
            writer.WriteByteArray2D(data.DynamicShrubberyDensity);
        }

        writer.Write(data.TextureCellCount);
        writer.Write((uint)data.BlendDescriptions.Count + 1);
        writer.Write(data.ParsedCliffTextureMappingsCount);
        writer.Write((uint)data.Textures.Count);

        foreach (var t in data.Textures)
        {
            writer.Write(t.CellStart);
            writer.Write(t.CellCount);
            writer.Write(t.CellSize);
            writer.Write(t.MagicValue);
            writer.WriteUInt16PrefixedAsciiString(t.Name);
        }

        writer.Write(data.MagicValue1);
        writer.Write(data.MagicValue2);

        foreach (var d in data.BlendDescriptions)
        {
            writer.Write(d.SecondaryTextureTile);
            writer.Write(d.RawBlendDirection);
            writer.Write(d.Flags);
            writer.Write(d.TwoSided);
            writer.Write(d.MagicValue1);
            writer.Write(0u);
        }

        foreach (var c in data.CliffTextureMappings)
        {
            writer.Write(c.TextureTile);
            writer.Write(c.BottomLeftCoords);
            writer.Write(c.BottomRightCoords);
            writer.Write(c.TopRightCoords);
            writer.Write(c.TopLeftCoords);
            writer.Write(c.Unknown2);
        }

        var endPos = stream.Position;
        var dataSize = (uint)(endPos - startPos);
        stream.Position = sizePos;
        writer.Write(dataSize);
        return stream.ToArray();
    }

    private static byte[] SerializeGlobalLighting(MapGlobalLightingRecord data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(data.Version);
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.Write((uint)data.Time);

        MapTimeOfDay[] times = [MapTimeOfDay.Morning, MapTimeOfDay.Afternoon, MapTimeOfDay.Evening, MapTimeOfDay.Night];
        foreach (var t in times)
        {
            if (data.Configurations.TryGetValue(t, out var config))
            {
                WriteLight(writer, config.TerrainSun);
                if (data.Version < 10)
                {
                    WriteLight(writer, config.ObjectSun);
                    if (data.Version >= 7)
                    {
                        WriteLight(writer, config.InfantrySun);
                    }
                }

                WriteLight(writer, config.TerrainAccent1);
                if (data.Version < 10)
                {
                    WriteLight(writer, config.ObjectAccent1);
                    if (data.Version >= 7)
                    {
                        WriteLight(writer, config.InfantryAccent1);
                    }
                }

                WriteLight(writer, config.TerrainAccent2);
                if (data.Version < 10)
                {
                    WriteLight(writer, config.ObjectAccent2);
                    if (data.Version >= 7)
                    {
                        WriteLight(writer, config.InfantryAccent2);
                    }
                }
            }
            else
            {
                WriteEmptyConfig(writer, data.Version);
            }
        }

        writer.Write(data.ShadowColorArgb);

        if (data.Version >= 7 && data.Version < 11 && data.Unknown is not null)
        {
            writer.Write(data.Unknown);
        }

        if (data.Version >= 12 && data.Unknown2.HasValue && data.Unknown3Argb.HasValue)
        {
            writer.Write(data.Unknown2.Value);
            writer.Write(data.Unknown3Argb.Value);
        }

        if (data.Version >= 8 && data.NoCloudFactor.HasValue)
        {
            writer.Write(data.NoCloudFactor.Value.X);
            writer.Write(data.NoCloudFactor.Value.Y);
            writer.Write(data.NoCloudFactor.Value.Z);
        }

        var endPos = stream.Position;
        var dataSize = (uint)(endPos - startPos);
        stream.Position = sizePos;
        writer.Write(dataSize);
        return stream.ToArray();
    }

    private static void WriteLight(BinaryWriter writer, MapGlobalLightRecord? light)
    {
        writer.Write(light?.Ambient ?? Vector3.Zero);
        writer.Write(light?.Color ?? Vector3.Zero);
        writer.Write(light?.Direction ?? Vector3.Zero);
    }

    private static void WriteEmptyConfig(BinaryWriter writer, ushort version)
    {
        for (var i = 0; i < 3; i++)
        {
            WriteLight(writer, null);
            if (version < 10)
            {
                WriteLight(writer, null);
                if (version >= 7)
                {
                    WriteLight(writer, null);
                }
            }
        }
    }

    private static byte[] SerializeNamedCameras(MapNamedCamerasRecord data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(data.Version);
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.Write((uint)data.Cameras.Count);
        foreach (var c in data.Cameras)
        {
            writer.Write(c.LookAtPoint);
            writer.WriteUInt16PrefixedAsciiString(c.Name);
            writer.Write(c.Pitch);
            writer.Write(c.Roll);
            writer.Write(c.Yaw);
            writer.Write(c.Zoom);
            writer.Write(c.FieldOfView);
            writer.Write(c.Unknown);
        }

        var endPos = stream.Position;
        var dataSize = (uint)(endPos - startPos);
        stream.Position = sizePos;
        writer.Write(dataSize);
        return stream.ToArray();
    }

    private static byte[] SerializeStandingWaterAreas(MapStandingWaterAreasRecord data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(data.Version);
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.Write((uint)data.Areas.Count);
        foreach (var a in data.Areas)
        {
            writer.Write(a.UniqueId);
            writer.WriteUInt16PrefixedAsciiString(a.Name);
            writer.WriteUInt16PrefixedAsciiString(a.LayerName);
            writer.Write(a.UvScrollSpeed);
            writer.Write(a.UseAdditiveBlending);
            writer.WriteUInt16PrefixedAsciiString(a.BumpMapTexture);
            writer.WriteUInt16PrefixedAsciiString(a.SkyTexture);
            writer.Write((uint)a.Points.Count);
            foreach (var p in a.Points)
            {
                writer.Write(p);
            }

            writer.Write(a.WaterHeight);
            writer.WriteUInt16PrefixedAsciiString(a.FxShader);
            writer.WriteUInt16PrefixedAsciiString(a.DepthColors);
        }

        var endPos = stream.Position;
        var dataSize = (uint)(endPos - startPos);
        stream.Position = sizePos;
        writer.Write(dataSize);
        return stream.ToArray();
    }

    private static byte[] SerializeStandingWaveAreas(MapStandingWaveAreasRecord data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(data.Version);
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.Write((uint)data.Areas.Count);
        foreach (var a in data.Areas)
        {
            writer.Write(a.UniqueId);
            writer.WriteUInt16PrefixedAsciiString(a.Name);
            writer.WriteUInt16PrefixedAsciiString(a.LayerName);
            writer.Write(a.UvScrollSpeed);
            writer.Write(a.UseAdditiveBlending);
            writer.Write((uint)a.Points.Count);
            foreach (var p in a.Points)
            {
                writer.Write(p);
            }

            writer.Write(0u);
            if (data.Version < 3)
            {
                writer.Write(a.FinalWidth);
                writer.Write(a.FinalHeight);
                writer.Write(a.InitialWidthFraction);
                writer.Write(a.InitialHeightFraction);
                writer.Write(a.InitialVelocity);
                writer.Write(a.TimeToFade);
                writer.Write(a.TimeToCompress);
                writer.Write(a.TimeOffset2ndWave);
                writer.Write(a.DistanceFromShore);
            }

            writer.WriteUInt16PrefixedAsciiString(a.Texture);
            if (data.Version == 2)
            {
                writer.WriteBooleanUInt32(a.EnablePcaWave);
            }

            if (data.Version >= 4)
            {
                writer.WriteUInt16PrefixedAsciiString(a.WaveParticleFXName ?? string.Empty);
            }
        }

        var endPos = stream.Position;
        var dataSize = (uint)(endPos - startPos);
        stream.Position = sizePos;
        writer.Write(dataSize);
        return stream.ToArray();
    }

    private static byte[] SerializeRiverAreas(MapRiverAreasRecord data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(data.Version);
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.Write((uint)data.Areas.Count);
        foreach (var a in data.Areas)
        {
            writer.Write(a.UniqueId);
            writer.WriteUInt16PrefixedAsciiString(a.Name);
            writer.WriteUInt16PrefixedAsciiString(a.LayerName);
            writer.Write(a.UvScrollSpeed);
            writer.Write(a.UseAdditiveBlending);
            writer.WriteUInt16PrefixedAsciiString(a.RiverTexture);
            writer.WriteUInt16PrefixedAsciiString(a.NoiseTexture);
            writer.WriteUInt16PrefixedAsciiString(a.AlphaEdgeTexture);
            writer.WriteUInt16PrefixedAsciiString(a.SparkleTexture);
            writer.Write(a.ColorR);
            writer.Write(a.ColorG);
            writer.Write(a.ColorB);
            writer.Write((byte)0);
            writer.Write(a.Alpha);
            writer.Write(a.WaterHeight);
            if (data.Version >= 3)
            {
                writer.WriteUInt16PrefixedAsciiString(a.RiverType);
            }

            writer.WriteUInt16PrefixedAsciiString(a.MinimumWaterLod);
            writer.Write((uint)a.Lines.Count);
            foreach (var l in a.Lines)
            {
                writer.Write(l.Start);
                writer.Write(l.End);
            }
        }

        var endPos = stream.Position;
        var dataSize = (uint)(endPos - startPos);
        stream.Position = sizePos;
        writer.Write(dataSize);
        return stream.ToArray();
    }

    private static byte[] SerializeGlobalWaterSettings(MapGlobalWaterSettingsRecord data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(data.Version);
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.WriteBooleanUInt32(data.ReflectionOn);
        writer.Write(data.ReflectionPlaneZ);

        var endPos = stream.Position;
        var dataSize = (uint)(endPos - startPos);
        stream.Position = sizePos;
        writer.Write(dataSize);
        return stream.ToArray();
    }

    private static byte[] SerializePolygonTriggers(MapPolygonTriggersRecord data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(data.Version);
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.Write((uint)data.Triggers.Count);
        foreach (var t in data.Triggers)
        {
            writer.WriteUInt16PrefixedAsciiString(t.Name);
            writer.WriteUInt16PrefixedAsciiString(t.LayerName ?? string.Empty);
            writer.Write(t.TriggerId);
            writer.Write(t.IsWater);
            writer.Write(t.IsRiver);
            writer.Write(t.RiverStart);
            if (data.Version >= 5)
            {
                writer.Write(t.UseAdditiveBlending);
                writer.Write(t.UvScrollSpeed);
                writer.WriteUInt16PrefixedAsciiString(t.RiverTexture ?? string.Empty);
                writer.WriteUInt16PrefixedAsciiString(t.NoiseTexture ?? string.Empty);
                writer.WriteUInt16PrefixedAsciiString(t.AlphaEdgeTexture ?? string.Empty);
                writer.WriteUInt16PrefixedAsciiString(t.SparkleTexture ?? string.Empty);
                writer.WriteUInt16PrefixedAsciiString(t.BumpMapTexture ?? string.Empty);
                writer.WriteUInt16PrefixedAsciiString(t.SkyTexture ?? string.Empty);
                writer.Write(t.Unknown);
                writer.Write(t.ColorR);
                writer.Write(t.ColorG);
                writer.Write(t.ColorB);
                writer.Write(t.RiverAlpha);
            }

            writer.Write((uint)t.Points.Count);
            foreach (var p in t.Points)
            {
                writer.Write((int)p.X);
                writer.Write((int)p.Y);
                writer.Write((int)p.Z);
            }
        }

        var endPos = stream.Position;
        var dataSize = (uint)(endPos - startPos);
        stream.Position = sizePos;
        writer.Write(dataSize);
        return stream.ToArray();
    }

    private static byte[] SerializeWaypointsList(MapWaypointPathsRecord data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(data.Version);
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.Write((uint)data.Paths.Count);
        foreach (var p in data.Paths)
        {
            writer.Write(p.StartWaypointId);
            writer.Write(p.EndWaypointId);
        }

        var endPos = stream.Position;
        var dataSize = (uint)(endPos - startPos);
        stream.Position = sizePos;
        writer.Write(dataSize);
        return stream.ToArray();
    }

    private static int CoreExtent(int latticePoints, int chunkIndex) => Math.Min(CorePointsPerAxis, latticePoints - (chunkIndex * IntervalsPerChunkAxis));

    private static string Describe(ChunkKey key) => $"{key.Cx}_{key.Cy}";
}
