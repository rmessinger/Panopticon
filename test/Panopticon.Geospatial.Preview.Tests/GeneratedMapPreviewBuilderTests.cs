using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Scripting;
using Xunit;

namespace Panopticon.Geospatial.Preview.Tests;

/// <summary>
/// <see cref="GeneratedMapPreviewBuilder"/>: resident MapFile conversion of a payload or
/// chunk-aligned crop, 16-bit height quantization with reported offsets and loss, texture
/// painting, mask conversion, and waypoint rules (B4b deep dive section 6.1, group 5
/// decisions 3 through 5).
/// </summary>
public sealed class GeneratedMapPreviewBuilderTests
{
    private const double Quantum = GeneratedMapPreviewBuilder.HeightQuantumGameUnits;

    [Fact]
    public void WholeMap_BuildsAndRoundTrips_RestoredHeightsWithinOneQuantum()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.NotNull(result.MapFile);
        Assert.NotNull(result.Transform);
        Assert.NotNull(result.Report);

        var map = result.MapFile!;
        Assert.Equal((uint)257, map.HeightMapData.Width);
        Assert.Equal((uint)257, map.HeightMapData.Height);
        Assert.Equal(0u, map.HeightMapData.BorderWidth);
        Assert.True(map.HeightMapData.ElevationsAre16Bit);

        // Whole-map crop minimum is the lattice minimum GlobalHeight(0, 0); the outer
        // aprons of the corner chunks never enter the assembled map.
        Assert.Equal(1000d, result.Transform!.CropMinimumGameZ, 6);
        Assert.Equal(0d, result.Transform.CropOriginGameX, 6);
        Assert.Equal(0d, result.Transform.CropOriginGameY, 6);
        Assert.Equal(0.0390625d, result.Transform.HeightQuantum, 10);
        Assert.Equal(0L, result.Report!.ClampedHeightSampleCount);
        Assert.True(result.Report.MaxQuantizationErrorGameUnits <= Quantum / 2d);

        var reloaded = WriteAndReload(map);
        for (var y = 0; y < 257; y++)
        {
            for (var x = 0; x < 257; x++)
            {
                var restored = (reloaded.HeightMapData.Elevations[x, y] * 0.0390625f) + 1000f;
                var expected = PreviewPayloadFixture.GlobalHeight(x, y);
                Assert.True(
                    Math.Abs(restored - expected) <= Quantum / 2d + 1e-9d,
                    $"({x}, {y}): restored {restored}, expected {expected}");
            }
        }

        // Painted tiles and masks survive the same round trip.
        for (var y = 0; y < 257; y += 17)
        {
            for (var x = 0; x < 257; x += 13)
            {
                Assert.Equal(ExpectedTileValue(x, y), reloaded.BlendTileData.Tiles[x, y]);
                Assert.Equal(x % 3 == 0, reloaded.BlendTileData.Impassability[x, y]);
            }
        }
    }

    [Fact]
    public void Crop_HalfOpenChunkRectangle_TranslatesOriginAndRebasesZ()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new List<GeneratedFeature>
        {
            PreviewPayloadFixture.Waypoint("wp-crop", 1900d, 2000d, 1350d, "gamma"),
        };
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap(), new PreviewCrop(1, 1, 2, 2));

        Assert.True(result.Success, FormatDiagnostics(result));
        var map = result.MapFile!;
        Assert.Equal((uint)129, map.HeightMapData.Width);
        Assert.Equal((uint)129, map.HeightMapData.Height);

        Assert.Equal(1280d, result.Transform!.CropOriginGameX, 6);
        Assert.Equal(1280d, result.Transform.CropOriginGameY, 6);
        Assert.Equal(1224d, result.Transform.CropMinimumGameZ, 6);

        for (var y = 0; y < 129; y += 16)
        {
            for (var x = 0; x < 129; x += 16)
            {
                var restored = (map.HeightMapData.Elevations[x, y] * 0.0390625f) + 1224f;
                var expected = PreviewPayloadFixture.GlobalHeight(128 + x, 128 + y);
                Assert.True(Math.Abs(restored - expected) <= Quantum / 2d + 1e-9d);
            }
        }

        // Engine XY is payload game XY minus the crop origin; Z keeps the rebased elevation.
        var gamma = FindWaypoint(map, "gamma");
        Assert.Equal(new Vector3(620f, 720f, (float)(1350d - 1224d)), gamma.Position);
    }

    [Fact]
    public void NonSquareCrop_KeepsIndependentAxes()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root, chunksX: 3, chunksY: 2);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal((uint)385, result.MapFile!.HeightMapData.Width);
        Assert.Equal((uint)257, result.MapFile.HeightMapData.Height);

        var farRestored = (result.MapFile.HeightMapData.Elevations[384, 256] * 0.0390625f) + 1000f;
        Assert.True(
            Math.Abs(farRestored - PreviewPayloadFixture.GlobalHeight(384, 256)) <= Quantum / 2d + 1e-9d,
            $"far corner restored {farRestored}");
    }

    [Fact]
    public void Border_IsIndexPaddingOnly_NearestEdgeFill()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new List<GeneratedFeature>
        {
            PreviewPayloadFixture.Waypoint("wp-border", 640d, 640d, 1100d, "alpha"),
        };
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var options = new PreviewBuildOptions { BorderWidth = 5 };
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap(), options: options);

        Assert.True(result.Success, FormatDiagnostics(result));
        var map = result.MapFile!;
        Assert.Equal((uint)267, map.HeightMapData.Width);
        Assert.Equal((uint)5, map.HeightMapData.BorderWidth);

        // Stored index is crop index + border and the engine XY of the stored core equals
        // crop XY directly, never shifted by border * 10.
        var alpha = FindWaypoint(map, "alpha");
        Assert.Equal(new Vector3(640f, 640f, 100f), alpha.Position);

        // Border cells extend the nearest core edge; corners take the core corner.
        Assert.Equal(map.HeightMapData.Elevations[5, 100], map.HeightMapData.Elevations[0, 100]);
        Assert.Equal(map.HeightMapData.Elevations[5, 5], map.HeightMapData.Elevations[0, 0]);
        Assert.Equal(map.HeightMapData.Elevations[261, 261], map.HeightMapData.Elevations[266, 266]);
        Assert.Equal(map.HeightMapData.Elevations[261, 5], map.HeightMapData.Elevations[266, 0]);
        Assert.Equal(ExpectedTileValue(0, 95), map.BlendTileData.Tiles[5, 100]);
        Assert.Equal(ExpectedTileValue(0, 0), map.BlendTileData.Tiles[0, 5]);

        foreach (var impassable in BorderCells(map))
        {
            Assert.False(impassable);
        }
    }

    [Fact]
    public void OversizedCrop_FailsWithTypedDiagnostic_NeverAutoCrops()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var options = new PreviewBuildOptions { MaxStoredHeightCells = 1000 };
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap(), options: options);

        Assert.False(result.Success);
        Assert.Null(result.MapFile);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PreviewDiagnosticCodes.PreviewOversized, diagnostic.Code);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
        Assert.Contains("never cropped automatically", diagnostic.Message);
    }

    [Fact]
    public void HeightClamp_IsReportedWithLoss()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(
            scratch.Root,
            heightOverride: (latticeX, latticeY) => PreviewPayloadFixture.GlobalHeight(latticeX, latticeY) + (latticeX >= 192 ? 4000f : 0f));

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.True(result.Report!.ClampedHeightSampleCount > 0);

        var expectedClamped = 0L;
        for (var y = 0; y < 257; y++)
        {
            for (var x = 0; x < 257; x++)
            {
                var height = PreviewPayloadFixture.GlobalHeight(x, y) + (x >= 192 ? 4000f : 0f);
                if (Math.Round((height - 1000d) / Quantum, MidpointRounding.AwayFromZero) > ushort.MaxValue)
                {
                    expectedClamped++;
                }
            }
        }

        Assert.Equal(expectedClamped, result.Report.ClampedHeightSampleCount);

        var farRestored = (result.MapFile!.HeightMapData.Elevations[256, 256] * 0.0390625f) + 1000f;
        Assert.Equal(1000d + (65535 * Quantum), farRestored, 6);
    }

    [Fact]
    public void Materials_PaintTextureTableAndTiles_FromPaletteBindings()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, FormatDiagnostics(result));
        var blend = result.MapFile!.BlendTileData;
        Assert.Equal(["GrassType1", "DirtMediumType1", "RockType1"], result.Report!.TextureNames);
        Assert.Equal(3, blend.Textures.Length);
        Assert.Equal("GrassType1", blend.Textures[0].Name);
        Assert.Equal("DirtMediumType1", blend.Textures[1].Name);
        Assert.Equal("RockType1", blend.Textures[2].Name);

        for (var y = 0; y < 257; y += 31)
        {
            for (var x = 0; x < 257; x += 31)
            {
                Assert.Equal(ExpectedTileValue(x, y), blend.Tiles[x, y]);
            }
        }
    }

    [Fact]
    public void Materials_DuplicateCanonicalTextures_DedupeIntoOneEntry()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var textureMap = PreviewTextureMap.FromJson(
            """{"grass": "GrassType1", "dirt": "grasstype1", "rock": "RockType1"}""",
            new PreviewPayloadFixture.FakeCatalog(),
            new GeoDiagnosticBag());
        Assert.NotNull(textureMap);

        var result = GeneratedMapPreviewBuilder.Build(reader, textureMap!);

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(["GrassType1", "RockType1"], result.Report!.TextureNames);

        // dirt (palette index 1) shares canonical texture 0; rock (index 2) is texture 1.
        Assert.Equal(0, result.MapFile!.BlendTileData.Tiles[1, 0]);
        Assert.Equal(64, result.MapFile.BlendTileData.Tiles[2, 0]);
        Assert.Equal(0, result.MapFile.BlendTileData.Tiles[0, 0]);
    }

    [Fact]
    public void Masks_ImpassablePaints_UnbuildableRetainedUnconverted()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, FormatDiagnostics(result));
        var blend = result.MapFile!.BlendTileData;

        var expectedImpassable = 0L;
        var expectedUnbuildable = 0L;
        for (var y = 0; y < 257; y++)
        {
            for (var x = 0; x < 257; x++)
            {
                Assert.Equal(x % 3 == 0, blend.Impassability[x, y]);
                if (x % 3 == 0)
                {
                    expectedImpassable++;
                }
                else if (y % 4 == 0)
                {
                    expectedUnbuildable++;
                }
            }
        }

        Assert.Equal(expectedImpassable, result.Report!.ImpassableCellCount);
        Assert.Equal(expectedUnbuildable, result.Report.UnrepresentedUnbuildableCellCount);
    }

    [Fact]
    public void PlayerStarts_MissingOrExcluded_KeepFactoryDefaults()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, FormatDiagnostics(result));
        var start1 = FindWaypoint(result.MapFile!, "Player_1_Start");
        var start2 = FindWaypoint(result.MapFile!, "Player_2_Start");

        Assert.Equal(new Vector3(640f, 640f, 0f), start1.Position);
        Assert.Equal(new Vector3(1920f, 1920f, 0f), start2.Position);
        Assert.Empty(result.Report!.OverriddenPlayerStartSlots);
    }

    [Fact]
    public void PlayerStarts_ReservedNames_OverrideFactorySlots_CaseInsensitive()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new List<GeneratedFeature>
        {
            PreviewPayloadFixture.Waypoint("wp-start-1", 500d, 300d, 1700d, "player_1_start"),
            PreviewPayloadFixture.Waypoint("wp-alpha", 640d, 1280d, 1100d, "alpha"),
        };
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, FormatDiagnostics(result));
        var map = result.MapFile!;

        // The canonical reserved name replaces the default slot at Z zero; slot 2 keeps
        // its factory default and no duplicate start objects remain.
        var starts = map.ObjectsList.Objects.Where(o => IsWaypointNamed(o, "Player_1_Start")).ToArray();
        Assert.Single(starts);
        Assert.Equal(new Vector3(500f, 300f, 0f), starts[0].Position);
        Assert.Equal(1, (int)starts[0].Properties["waypointID"].Value);

        var start2 = FindWaypoint(map, "Player_2_Start");
        Assert.Equal(new Vector3(1920f, 1920f, 0f), start2.Position);
        Assert.Equal([1], result.Report!.OverriddenPlayerStartSlots);
    }

    [Fact]
    public void Waypoints_DuplicateAuthoredNames_RejectWithTypedDiagnostic()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new List<GeneratedFeature>
        {
            PreviewPayloadFixture.Waypoint("wp-1", 100d, 100d, 1100d, "alpha"),
            PreviewPayloadFixture.Waypoint("wp-2", 200d, 200d, 1150d, "alpha"),
        };
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap());

        Assert.False(result.Success);
        Assert.Null(result.MapFile);
        Assert.Contains(result.Diagnostics, d => d.Code == PreviewDiagnosticCodes.WaypointNameDuplicate);
    }

    [Fact]
    public void Waypoints_DuplicateReservedStarts_RejectCaseInsensitively()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new List<GeneratedFeature>
        {
            PreviewPayloadFixture.Waypoint("wp-1", 100d, 100d, 1100d, "Player_1_Start"),
            PreviewPayloadFixture.Waypoint("wp-2", 200d, 200d, 1150d, "PLAYER_1_START"),
        };
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap());

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == PreviewDiagnosticCodes.WaypointNameDuplicate);
    }

    [Fact]
    public void Waypoints_OuterEdgeIncluded_BeyondLatticeExcludedAndCounted()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new List<GeneratedFeature>
        {
            PreviewPayloadFixture.Waypoint("wp-edge", 2560d, 2560d, 1100d, "edge"),
            PreviewPayloadFixture.Waypoint("wp-beyond", 2565d, 100d, 1100d, "beyond"),
        };
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(new Vector3(2560f, 2560f, 100f), FindWaypoint(result.MapFile!, "edge").Position);
        Assert.Null(TryFindWaypoint(result.MapFile!, "beyond"));
        Assert.Equal(1, result.Report!.ExcludedWaypointCount);
        Assert.Equal(1, result.Report.ConvertedWaypointCount);
    }

    [Fact]
    public void Waypoints_FeatureIdFallback_WhenNameAttributeMissing()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new List<GeneratedFeature>
        {
            PreviewPayloadFixture.Waypoint("wp-named-by-id", 300d, 300d, 1100d),
        };
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.NotNull(TryFindWaypoint(result.MapFile!, "wp-named-by-id"));
    }

    [Fact]
    public void UnrepresentedFeatures_AreCountedByKind()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(reader, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, FormatDiagnostics(result));
        var counts = result.Report!.UnrepresentedFeatureCounts.ToDictionary(count => count.Kind, count => count.Count);
        Assert.Equal(1L, counts[GeneratedFeatureKind.Landmark]);
        Assert.Equal(1L, counts[GeneratedFeatureKind.Annotation]);
        Assert.Equal(0L, counts[GeneratedFeatureKind.Waypoint]);
    }

    [Fact]
    public void MissingPaletteBinding_Fails()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var textureMap = PreviewTextureMap.FromJson(
            """{"grass": "GrassType1", "dirt": "DirtMediumType1"}""",
            new PreviewPayloadFixture.FakeCatalog(),
            new GeoDiagnosticBag());
        Assert.NotNull(textureMap);

        var result = GeneratedMapPreviewBuilder.Build(reader, textureMap!);

        Assert.False(result.Success);
        Assert.Null(result.MapFile);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PreviewDiagnosticCodes.TextureMapMissingBinding, diagnostic.Code);
        Assert.Contains("rock", diagnostic.Message);
    }

    [Fact]
    public void UnknownUsedTextureName_Fails()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var textureMap = PreviewTextureMap.FromJson(
            """{"grass": "GrassType1", "dirt": "DirtMediumType1", "rock": "NotInCatalog"}""",
            new PreviewPayloadFixture.FakeCatalog(),
            new GeoDiagnosticBag());
        Assert.NotNull(textureMap);

        var result = GeneratedMapPreviewBuilder.Build(reader, textureMap!);

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PreviewDiagnosticCodes.TextureMapUnknownTexture, diagnostic.Code);
    }

    [Theory]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0, 0, 3, 2)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(0, -1, 1, 1)]
    public void InvalidCrop_RejectsWithTypedDiagnostic(int loX, int loY, int hiX, int hiY)
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);

        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var result = GeneratedMapPreviewBuilder.Build(
            reader,
            PreviewPayloadFixture.DefaultTextureMap(),
            new PreviewCrop(loX, loY, hiX, hiY));

        Assert.False(result.Success);
        Assert.Null(result.MapFile);
        Assert.Contains(result.Diagnostics, d => d.Code == PreviewDiagnosticCodes.CropInvalid);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);

        Assert.Throws<ArgumentNullException>(() => GeneratedMapPreviewBuilder.Build(null!, PreviewPayloadFixture.DefaultTextureMap()));
        Assert.Throws<ArgumentNullException>(() => GeneratedMapPreviewBuilder.Build(reader, null!));
    }

    private static ushort ExpectedTileValue(long latticeX, long latticeY)
    {
        return (ushort)(PreviewPayloadFixture.GlobalMaterial(latticeX, latticeY) * 64);
    }

    private static IEnumerable<bool> BorderCells(MapFile map)
    {
        var border = map.HeightMapData.BorderWidth;
        for (var y = 0; y < map.HeightMapData.Height; y++)
        {
            for (var x = 0; x < map.HeightMapData.Width; x++)
            {
                if (x < border || x >= map.HeightMapData.Width - border || y < border || y >= map.HeightMapData.Height - border)
                {
                    yield return map.BlendTileData.Impassability[x, y];
                }
            }
        }
    }

    private static MapObject FindWaypoint(MapFile map, string name)
    {
        return TryFindWaypoint(map, name) ?? throw new InvalidOperationException($"Waypoint '{name}' not found.");
    }

    private static MapObject? TryFindWaypoint(MapFile map, string name)
    {
        return map.ObjectsList.Objects.FirstOrDefault(o => IsWaypointNamed(o, name));
    }

    private static bool IsWaypointNamed(MapObject mapObject, string name)
    {
        return mapObject.TypeName == Waypoint.ObjectTypeName
            && mapObject.Properties.TryGetValue("waypointName", out var property)
            && property.Value is string waypointName
            && waypointName == name;
    }

    private static string FormatDiagnostics(GeneratedMapPreviewResult result)
    {
        return string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
    }

    private static MapFile WriteAndReload(MapFile map)
    {
        byte[] written;
        using (var stream = new MemoryStream())
        {
            map.WriteTo(stream);
            written = stream.ToArray();
        }

        return MapFile.FromStream(new MemoryStream(written));
    }
}
