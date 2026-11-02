using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Data.Map;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Pipeline;
using Panopticon.Geospatial.Preview;
using Panopticon.Scripting;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// CPU verification of the card 24 synthetic fixture against the real public pipeline:
/// <see cref="GeospatialImporter"/> builds a payload from the fixture's BCL TIFF + KML
/// sources, and the accepted <see cref="GeneratedMapPreviewBuilder"/> converts the planned
/// south-east crop into a map whose heights, quantization, mask, palette, and waypoints all
/// match the fixture's closed-form expectations. No Game instance, graphics device, CLI
/// verb, or operator location is involved; every expectation comes from
/// <see cref="Card24ProofFixture"/> analytic members, never from pipeline output. The
/// 300-frame GameFact proof is deliberately not implemented here: it depends on the card 24
/// CLI preview acceptance and will be added to this unit afterwards.
/// </summary>
public class Card24ProofFixtureTests
{
    private const double HeightToleranceGameUnits = 0.01d;

    private const int CropOriginChunkX = 1;

    private const int CropBaseX = CropOriginChunkX * ChunkLayout.IntervalsPerChunk;

    /// <summary>Crop core width minus one: the forced eastern edge node column.</summary>
    private const int StoredEdgeIndex = 128;

    private readonly ITestOutputHelper _output;

    public Card24ProofFixtureTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static GeneratedMapPayloadReader OpenImportedPayload(Card24ProofFixture fixture, out GeoImportResult import)
    {
        import = GeospatialImporter.Run(fixture.ManifestPath, Path.Combine(fixture.Root, "out"));

        Assert.True(import.Success, $"Import failed: {string.Join("; ", import.Diagnostics.Select(d => $"{d.Code}: {d.Message}"))}");
        Assert.DoesNotContain(import.Diagnostics, d => d.Severity == GeoSeverity.Error);
        Assert.Equal(Path.Combine(fixture.Root, "out"), import.PayloadPath);

        var diagnostics = new GeoDiagnosticBag();
        var reader = GeneratedMapPayloadReader.Open(import.PayloadPath!, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(reader);

        return reader!;
    }

    [Fact]
    public void FixtureDem_ParsesWithRealReader_AndCarriesAnalyticSurface()
    {
        using var fixture = Card24ProofFixture.Create();

        var diagnostics = new GeoDiagnosticBag();
        var raster = new Panopticon.Geospatial.Readers.GeoTiffRasterReader().Read(
            Path.Combine(fixture.Root, "dem", "dem.tif"),
            new GeospatialImportLimits(),
            diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.Equal(Card24ProofFixture.DemCells, raster!.Width);
        Assert.Equal(Card24ProofFixture.DemCells, raster.Height);
        Assert.Equal(RasterSampleFormat.Float32, raster.SampleFormat);
        Assert.True(raster.Transform.IsNorthUp);
        Assert.Equal(CoordinateSystemId.Utm(31, northernHemisphere: true), raster.CoordinateSystem);
        Assert.Equal("EGM96", raster.VerticalDatum);

        // Raster cell (column 194, row 194) coincides with the ridge-peak lattice node
        // (1920, 640): cell centers run -20 + 10*column and 2580 - 10*row.
        Assert.Equal(Card24ProofFixture.ExpectedHeightAtLattice(192, 64), raster.Get(0, 194, 194));

        // The four raster corners carry the analytic surface at the corner cell centers.
        var last = Card24ProofFixture.DemCells - 1;
        Assert.Equal(Height(-20d, 2580d), raster.Get(0, 0, 0), 5);
        Assert.Equal(Height(2580d, 2580d), raster.Get(0, 0, last), 5);
        Assert.Equal(Height(-20d, -20d), raster.Get(0, last, 0), 5);
        Assert.Equal(Height(2580d, -20d), raster.Get(0, last, last), 5);

        return;

        static float Height(double relativeX, double relativeY)
        {
            return (float)Card24ProofFixture.SurfaceHeight(relativeX, relativeY);
        }
    }

    [Fact]
    public void Import_BuildsTwoByTwoChunkPayload_MatchingAnalyticSurface()
    {
        using var fixture = Card24ProofFixture.Create();
        using var reader = OpenImportedPayload(fixture, out _);

        var grid = reader.Metadata.Grid;
        Assert.Equal(2, grid.ChunksX);
        Assert.Equal(2, grid.ChunksY);

        var transform = grid.Transform;
        Assert.Equal(Card24ProofFixture.MetersPerGameUnit, transform.MetersPerGameUnit);
        Assert.Equal(Card24ProofFixture.VerticalExaggeration, transform.VerticalExaggeration);
        Assert.Equal(Card24ProofFixture.VerticalOriginMeters, transform.VerticalOriginMeters);
        Assert.Equal("EGM96", transform.VerticalDatum);
        Assert.Equal(CoordinateSystemId.Utm(31, northernHemisphere: true), transform.ProjectedCoordinateSystem);
        Assert.Equal(Card24ProofFixture.Easting0, transform.OriginEastingNorthing.X, 0.001d);
        Assert.Equal(Card24ProofFixture.Northing0, transform.OriginEastingNorthing.Y, 0.001d);

        Assert.Equal(new[] { "grass", "paved" }, reader.Metadata.Palette.Names);

        // Interior lattice probes, analytic plane and ridge peak, away from every boundary.
        var diagnostics = new GeoDiagnosticBag();
        foreach (var (gameX, gameY) in new[] { (1920d, 640d), (1300d, 100d), (2500d, 1200d), (300d, 2200d) })
        {
            Assert.True(
                reader.TrySampleHeight(gameX, gameY, out var gameZ, diagnostics),
                $"Height sampling failed at ({gameX}, {gameY}).");
            Assert.Equal(Card24ProofFixture.SurfaceHeight(gameX, gameY), gameZ, HeightToleranceGameUnits);
        }

        Assert.False(diagnostics.HasErrors);

        // Stored chunk samples, aprons included, carry the analytic lattice heights. For
        // chunk (cx, cy), stored (sx, sy) is global lattice (cx * 128 + sx - 1, cy * 128 + sy - 1).
        var cropTerrain = reader.ReadTerrain(1, 0, diagnostics);
        Assert.NotNull(cropTerrain);
        Assert.Equal(Card24ProofFixture.ExpectedHeightAtLattice(192, 64), cropTerrain!.GetHeight(65, 65), 3);
        Assert.Equal(Card24ProofFixture.ExpectedHeightAtLattice(129, 1), cropTerrain.GetHeight(2, 2), 3);
        Assert.Equal(Card24ProofFixture.ExpectedHeightAtLattice(127, 64), cropTerrain.GetHeight(0, 65), 3);

        var northWestTerrain = reader.ReadTerrain(0, 1, diagnostics);
        Assert.NotNull(northWestTerrain);
        Assert.Equal(Card24ProofFixture.ExpectedHeightAtLattice(64, 128), northWestTerrain!.GetHeight(65, 1), 3);
        Assert.Equal(Card24ProofFixture.ExpectedHeightAtLattice(64, 192), northWestTerrain.GetHeight(65, 65), 3);
        Assert.False(diagnostics.HasErrors);

        // Independent source-file hashes: the payload provenance must carry the exact bytes
        // the fixture wrote, and the manifest hash must match the manifest on disk.
        Assert.Equal(fixture.ManifestSha256Hex, reader.Metadata.ManifestSha256Hex);
        foreach (var provenance in reader.Metadata.Provenance)
        {
            Assert.Equal(fixture.SourceSha256Hex[provenance.SourceId], provenance.Sha256Hex);
            Assert.Equal(
                new FileInfo(Path.Combine(fixture.Root, provenance.RelativePath)).Length,
                provenance.LengthBytes);
        }
    }

    [Fact]
    public void PreviewBuilder_SouthEastCrop_MatchesAnalyticExpectations()
    {
        using var fixture = Card24ProofFixture.Create();
        using var reader = OpenImportedPayload(fixture, out _);

        var textureMap = PreviewTextureMap.FromJson(
            File.ReadAllText(fixture.TexturesJsonPath),
            new KnownNameCatalog(),
            new GeoDiagnosticBag());

        Assert.NotNull(textureMap);

        var result = GeneratedMapPreviewBuilder.Build(reader, textureMap!, Card24ProofFixture.Crop);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.NotNull(result.MapFile);
        Assert.NotNull(result.Transform);
        Assert.NotNull(result.Report);

        var mapFile = result.MapFile!;
        var transform = result.Transform!;
        var report = result.Report!;
        var cropMinimum = Card24ProofFixture.ExpectedCropMinimumGameZ();

        // The crop origin is deliberately non-zero on X: rebase and translation are both exercised.
        Assert.Equal(1280d, transform.CropOriginGameX);
        Assert.Equal(0d, transform.CropOriginGameY);
        Assert.Equal(cropMinimum, transform.CropMinimumGameZ, 1e-6d);
        Assert.Equal(GeneratedMapPreviewBuilder.HeightQuantumGameUnits, transform.HeightQuantum);

        Assert.Equal(129u, report.StoredWidth);
        Assert.Equal(129u, report.StoredHeight);
        Assert.Equal(0u, report.BorderWidth);
        Assert.Equal(0L, report.ClampedHeightSampleCount);
        Assert.True(
            report.MaxQuantizationErrorGameUnits <= (GeneratedMapPreviewBuilder.HeightQuantumGameUnits / 2d) + 1e-9d,
            $"Max quantization error {report.MaxQuantizationErrorGameUnits} exceeds half a quantum.");
        Assert.Equal(3, report.ConvertedWaypointCount);
        Assert.Equal(1, report.ExcludedWaypointCount);
        Assert.Equal(new[] { 1, 2 }, report.OverriddenPlayerStartSlots);

        // Palette-order canonical texture table reaches both the report and the map.
        Assert.Equal(new[] { Card24ProofFixture.GrassTextureName, Card24ProofFixture.PavedTextureName }, report.TextureNames);
        Assert.Equal(
            new[] { Card24ProofFixture.GrassTextureName, Card24ProofFixture.PavedTextureName },
            mapFile.BlendTileData.Textures.Select(texture => texture.Name).ToArray());

        // The four crop corners plus the ridge peak restore the analytic surface within quantization.
        AssertStoredHeight(mapFile, 0, 0, ExpectedQuantized(128, 0, cropMinimum));
        AssertStoredHeight(mapFile, 128, 0, ExpectedQuantized(256, 0, cropMinimum));
        AssertStoredHeight(mapFile, 0, 128, ExpectedQuantized(128, 128, cropMinimum));
        AssertStoredHeight(mapFile, 128, 128, ExpectedQuantized(256, 128, cropMinimum));
        AssertStoredHeight(mapFile, 64, 64, ExpectedQuantized(192, 64, cropMinimum));

        var restored = mapFile.HeightMapData.Elevations[64, 64] * mapFile.HeightMapData.VerticalScale + transform.CropMinimumGameZ;
        Assert.Equal(Card24ProofFixture.SurfaceHeight(1920d, 640d), restored, GeneratedMapPreviewBuilder.HeightQuantumGameUnits / 2d + 1e-9d);

        // Engine coordinates are payload lattice minus the crop origin 1280. The eastern
        // edge column holds nodes at or over the extent east bound, the southern edge row
        // holds nodes on or outside the min-corner anchored extent south edge, so the
        // playable-area stage forces both impassable, and the plaza paints the nodes
        // strictly inside its ring.
        Assert.True(mapFile.BlendTileData.Impassability[42, 60]);
        Assert.True(mapFile.BlendTileData.Impassability[128, 50]);
        Assert.False(mapFile.BlendTileData.Impassability[2, 20]);
        Assert.Equal(64, (int)mapFile.BlendTileData.Tiles[42, 60]);
        Assert.Equal(0, (int)mapFile.BlendTileData.Tiles[2, 20]);

        var mismatches = new List<string>();
        long mapImpassable = 0;
        for (var y = 0; y < 129; y++)
        {
            for (var x = 0; x < 129; x++)
            {
                var actual = mapFile.BlendTileData.Impassability[x, y];
                var expected = (x == StoredEdgeIndex) || (y == 0) || Card24ProofFixture.IsPlazaLattice(x + CropBaseX, y);
                if (actual)
                {
                    mapImpassable++;
                }

                if (actual != expected)
                {
                    mismatches.Add($"engine ({x * 10}, {y * 10}): map {actual}, expected {expected}");
                }
            }
        }

        _output.WriteLine($"Map impassable {mapImpassable}, report {report.ImpassableCellCount}, analytic {Card24ProofFixture.ExpectedImpassableCellCount()}.");
        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} impassable mismatches, first 10: {string.Join("; ", mismatches.Take(10))}");
        Assert.Equal(Card24ProofFixture.ExpectedImpassableCellCount(), mapImpassable);
        Assert.Equal(Card24ProofFixture.ExpectedImpassableCellCount(), report.ImpassableCellCount);

        // Waypoints: authored player starts override the factory slots at Z 0, the ordinary
        // waypoint keeps its rebased elevation, and the out-of-crop waypoint is excluded.
        var playerOne = FindWaypoint(mapFile, "Player_1_Start");
        Assert.Equal(new Vector3(220f, 300f, 0f), playerOne!.Position);
        var playerTwo = FindWaypoint(mapFile, "Player_2_Start");
        Assert.Equal(new Vector3(1020f, 900f, 0f), playerTwo!.Position);

        var ridgePeak = FindWaypoint(mapFile, "ridgepeak");
        Assert.NotNull(ridgePeak);
        var expectedRidgeZ = (float)((double)Card24ProofFixture.ExpectedHeightAtLattice(192, 64) - cropMinimum);
        Assert.Equal(640f, ridgePeak!.Position.X);
        Assert.Equal(640f, ridgePeak.Position.Y);
        Assert.Equal(expectedRidgeZ, ridgePeak.Position.Z, 4);

        Assert.Null(FindWaypoint(mapFile, "farpoint"));
    }

    [Fact]
    public void PreviewTextureMap_ResolvesFixtureBindings_CaseInsensitively()
    {
        var json = """
            {
              "grass": "grasstype1",
              "paved": "asphalttype1"
            }
            """;

        var textureMap = PreviewTextureMap.FromJson(json, new KnownNameCatalog(), new GeoDiagnosticBag());

        Assert.NotNull(textureMap);
        Assert.Equal(PreviewTextureResolution.Resolved, textureMap!.TryResolve("grass", out var grass));
        Assert.Equal(Card24ProofFixture.GrassTextureName, grass);
        Assert.Equal(PreviewTextureResolution.Resolved, textureMap.TryResolve("paved", out var paved));
        Assert.Equal(Card24ProofFixture.PavedTextureName, paved);
    }

    /// <summary>
    /// The fixture's texture bindings are real Generals Terrain.ini block names. This runs
    /// only when an installation is discoverable (no Game, no graphics device, data-only
    /// catalog) and asserts exactly what the accepted catalog returned, never a hardcoded
    /// claim about the installed game.
    /// </summary>
    [GameFact(SageGame.CncGenerals)]
    public void FixtureTextureBindings_ExistInRealInstallCatalog()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        var names = TerrainTextureCatalog.LoadNames(installation);

        Assert.Contains(Card24ProofFixture.GrassTextureName, names);
        Assert.Contains(Card24ProofFixture.PavedTextureName, names);
    }

    private static void AssertStoredHeight(MapFile mapFile, int x, int y, ushort expected)
    {
        Assert.Equal(expected, mapFile.HeightMapData.Elevations[x, y]);
    }

    private static ushort ExpectedQuantized(int gx, int gy, float cropMinimum)
    {
        var height = Card24ProofFixture.ExpectedHeightAtLattice(gx, gy);
        var levels = Math.Round(
            ((double)height - cropMinimum) / GeneratedMapPreviewBuilder.HeightQuantumGameUnits,
            MidpointRounding.AwayFromZero);

        return (ushort)levels;
    }

    private static MapObject? FindWaypoint(MapFile mapFile, string name)
    {
        foreach (var mapObject in mapFile.ObjectsList.Objects)
        {
            if (mapObject.TypeName == Waypoint.ObjectTypeName
                && mapObject.Properties.TryGetValue("waypointName", out var property)
                && property.Value is string value
                && value == name)
            {
                return mapObject;
            }
        }

        return null;
    }

    private sealed class KnownNameCatalog : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames { get; } =
            new[] { Card24ProofFixture.GrassTextureName, Card24ProofFixture.PavedTextureName };
    }
}
