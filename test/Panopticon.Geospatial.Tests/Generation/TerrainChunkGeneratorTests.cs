#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Projection;
using Panopticon.Geospatial.Sampling;
using Panopticon.Geospatial.Tests.Sampling;
using Xunit;

namespace Panopticon.Geospatial.Tests.Generation;

public class TerrainChunkGeneratorTests
{
    private const double OriginEasting = 100000d;

    private const double OriginNorthing = 6000000d;

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    private static readonly CoordinateSystemId Tm4 = CoordinateSystemId.TransverseMercator(4d, 1d, 0d, 0d);

    [Fact]
    public void StoredSamplesMatchTheGlobalLatticeAndTheHeightRangeIncludesAprons()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var chunk = CreateGenerator().Generate(grid!, CenterAlignedElevation(1, 1, southUp: false), new SamplingOptions(), 0, 0);

        Assert.NotNull(chunk);

        var stored = ChunkLayout.StoredPointsPerChunkAxis;
        for (var sy = 0; sy < stored; sy++)
        {
            for (var sx = 0; sx < stored; sx++)
            {
                // Stored position (sx, sy) is global sample (sx - 1, sy - 1): the apron
                // sits outside the chunk core on every side.
                Assert.Equal(
                    (float)PlaneHeightMetersAtSample(sx - 1, sy - 1),
                    chunk!.GetHeight(sx, sy));
            }
        }

        // The plane rises east and north, so both extremes sit on apron samples beyond
        // the chunk's own 129 x 129 core: min at the southwest apron corner, max at the
        // northeast apron corner.
        Assert.Equal((float)PlaneHeightMetersAtSample(-1, -1), chunk!.MinHeightGameUnits);
        Assert.Equal((float)PlaneHeightMetersAtSample(stored - 2, stored - 2), chunk.MaxHeightGameUnits);
    }

    [Fact]
    public void AdjacentChunksShareBitIdenticalCoreEdgesAndAprons()
    {
        var grid = CreateGrid(widthMeters: 2560d, heightMeters: 2560d);
        Assert.NotNull(grid);

        var elevation = CenterAlignedElevation(2, 2, southUp: false);
        var generator = CreateGenerator();
        var chunks = new Dictionary<(int X, int Y), TerrainChunk>();
        for (var cy = 0; cy < 2; cy++)
        {
            for (var cx = 0; cx < 2; cx++)
            {
                var chunk = generator.Generate(grid!, elevation, new SamplingOptions(), cx, cy);
                Assert.NotNull(chunk);
                chunks[(cx, cy)] = chunk!;
            }
        }

        var southWest = chunks[(0, 0)];
        var southEast = chunks[(1, 0)];
        var northWest = chunks[(0, 1)];

        for (var sy = 0; sy < ChunkLayout.StoredPointsPerChunkAxis; sy++)
        {
            // Shared core edge at global sample 128: chunk 0's column 129, chunk 1's column 1.
            Assert.Equal(
                Bits(southWest.GetHeight(129, sy)),
                Bits(southEast.GetHeight(1, sy)));

            // Chunk 1's western apron repeats chunk 0's interior column at global 127.
            Assert.Equal(
                Bits(southWest.GetHeight(128, sy)),
                Bits(southEast.GetHeight(0, sy)));
        }

        for (var sx = 0; sx < ChunkLayout.StoredPointsPerChunkAxis; sx++)
        {
            Assert.Equal(
                Bits(southWest.GetHeight(sx, 129)),
                Bits(northWest.GetHeight(sx, 1)));

            Assert.Equal(
                Bits(southWest.GetHeight(sx, 128)),
                Bits(northWest.GetHeight(sx, 0)));
        }
    }

    [Fact]
    public void SouthUpAndNorthUpRastersProduceIdenticalHeightsOnATiltedPlane()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var generator = CreateGenerator();
        var north = generator.Generate(grid!, CenterAlignedElevation(1, 1, southUp: false), new SamplingOptions(), 0, 0);
        var south = generator.Generate(grid!, CenterAlignedElevation(1, 1, southUp: true), new SamplingOptions(), 0, 0);

        Assert.NotNull(north);
        Assert.NotNull(south);

        // A tilted plane distinguishes every row and column, so a raster that was
        // mirrored instead of flipped would fail the comparison.
        Assert.NotEqual(south!.GetHeight(5, 5), south.GetHeight(6, 6));
        Assert.NotEqual(south.GetHeight(5, 5), south.GetHeight(5, 6));

        var stored = ChunkLayout.StoredPointsPerChunkAxis;
        for (var sy = 0; sy < stored; sy++)
        {
            for (var sx = 0; sx < stored; sx++)
            {
                Assert.Equal(Bits(north!.GetHeight(sx, sy)), Bits(south!.GetHeight(sx, sy)));
            }
        }

        Assert.Equal((float)PlaneHeightMetersAtSample(64, 63), south.GetHeight(65, 64));
    }

    [Fact]
    public void VerticalOriginExaggerationAndMetersPerGameUnitConvertHeights()
    {
        // Half-meter game units halve the sample spacing to 5 m against the 10 m source
        // GSD, so probes land both on cell centers and between them.
        var grid = CreateGrid(
            widthMeters: 640d,
            heightMeters: 640d,
            metersPerGameUnit: 0.5d,
            verticalPolicy: new VerticalPolicy(100d, 2d));
        Assert.NotNull(grid);
        Assert.Equal(1, grid!.ChunksX);
        Assert.Equal(1, grid.ChunksY);

        var chunk = CreateGenerator().Generate(grid, CenterAlignedElevation(1, 1, southUp: false), new SamplingOptions(), 0, 0);

        Assert.NotNull(chunk);

        foreach (var (sx, sy) in new[] { (0, 0), (1, 1), (65, 64), (130, 130), (37, 91) })
        {
            var meters = PlaneHeightMetersAtSample(sx - 1, sy - 1, spacing: 5d);
            var expected = (float)((meters - 100d) * 2d / 0.5d);
            Assert.Equal(expected, chunk!.GetHeight(sx, sy));
        }
    }

    [Fact]
    public void TwoRunsProduceIdenticalChunksAndPinnedLittleEndianHashes()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var elevation = CenterAlignedElevation(1, 1, southUp: false);
        var first = CreateGenerator().Generate(grid!, elevation, new SamplingOptions(), 0, 0);
        var second = new TerrainChunkGenerator(SamplingTestSupport.IdentityTransformer())
            .Generate(grid!, elevation, new SamplingOptions(), 0, 0);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Heights.ToArray(), second!.Heights.ToArray());
        Assert.Equal(first.ContentSha256Hex, second.ContentSha256Hex);

        // Recompute the hash from explicit little-endian bytes, independent of the
        // generator, pinning the serialized byte order.
        var bytes = new byte[first.Heights.Length * 4];
        for (var i = 0; i < first.Heights.Length; i++)
        {
            var bits = BitConverter.SingleToInt32Bits(first.Heights.Span[i]);
            bytes[(i * 4) + 0] = (byte)bits;
            bytes[(i * 4) + 1] = (byte)(bits >> 8);
            bytes[(i * 4) + 2] = (byte)(bits >> 16);
            bytes[(i * 4) + 3] = (byte)(bits >> 24);
        }

        var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(expected, first.ContentSha256Hex);
    }

    [Fact]
    public void NoDataUnderTheFailPolicyRejectsTheChunkWithTypedDiagnostics()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var elevation = CenterAlignedElevation(1, 1, southUp: true, noDataCell: (Column: 66, Row: 68));
        var diagnostics = new GeoDiagnosticBag();
        var stats = new SamplingStats();

        var chunk = CreateGenerator().Generate(grid!, elevation, new SamplingOptions(), 0, 0, stats, diagnostics);

        Assert.Null(chunk);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Items, d => d.Code == SamplingDiagnosticCodes.NoDataPolicyFailure);
        Assert.Contains(
            diagnostics.Items,
            d => d.Code == GeoDiagnosticCodes.GenerateFailure
                && d.Stage == GeoStage.Generate
                && d.Severity == GeoSeverity.Error);
        Assert.Equal(1, stats.FailedSampleCount);
    }

    [Fact]
    public void CoverageMissingOutsideTheRasterRejectsTheChunkWithTypedDiagnostics()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        // Width 130 covers global samples -1 through 128 only; the eastern apron sample
        // at global 129 falls outside the source footprint.
        var elevation = CenterAlignedElevation(1, 1, southUp: true, widthOverride: 130);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(grid!, elevation, new SamplingOptions(), 0, 0, diagnostics: diagnostics);

        Assert.Null(chunk);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Items, d => d.Code == SamplingDiagnosticCodes.EdgePolicyFailure);
        Assert.Contains(diagnostics.Items, d => d.Code == GeoDiagnosticCodes.GenerateFailure);
    }

    [Fact]
    public void HeightsThatOverflowToNonFiniteAreRejected()
    {
        var grid = CreateGrid(verticalPolicy: new VerticalPolicy(0d, 1e300d));
        Assert.NotNull(grid);

        var elevation = CenterAlignedElevation(1, 1, southUp: false, constant: 1e300d);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(grid!, elevation, new SamplingOptions(), 0, 0, diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(GeoDiagnosticCodes.GenerateFailure, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.Contains("not finite", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HeightsBeyondTheFloat32RangeAreRejectedWithoutClamping()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var elevation = CenterAlignedElevation(1, 1, southUp: false, constant: 1e300d);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(grid!, elevation, new SamplingOptions(), 0, 0, diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(GeoDiagnosticCodes.GenerateFailure, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.Contains("float32", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CancelledTokenThrowsBeforeTheFirstRow()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => CreateGenerator().Generate(
                grid!,
                CenterAlignedElevation(1, 1, southUp: false),
                new SamplingOptions(),
                0,
                0,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void CancellationDuringStreamingStopsAtTheNextChunkRow()
    {
        var grid = CreateThreeByTwoGrid();
        var elevation = CenterAlignedElevation(3, 2, southUp: false);
        using var cancellation = new CancellationTokenSource();

        var enumerator = CreateGenerator()
            .GenerateAll(grid, elevation, new SamplingOptions(), cancellationToken: cancellation.Token)
            .GetEnumerator();
        using (enumerator)
        {
            Assert.True(enumerator.MoveNext());
            Assert.Equal((0, 0), (enumerator.Current.ChunkX, enumerator.Current.ChunkY));

            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => enumerator.MoveNext());
        }
    }

    [Fact]
    public void GenerateAllStreamsSouthwestRowMajorWithoutRetainingChunks()
    {
        var grid = CreateThreeByTwoGrid();
        var elevation = CenterAlignedElevation(3, 2, southUp: false);
        var report = new TerrainGenerationReport();
        var order = new List<(int X, int Y)>();

        var enumerator = CreateGenerator().GenerateAll(grid, elevation, new SamplingOptions(), report).GetEnumerator();
        using (enumerator)
        {
            for (var i = 0; i < 2; i++)
            {
                Assert.True(enumerator.MoveNext());
                order.Add((enumerator.Current.ChunkX, enumerator.Current.ChunkY));
            }
        }

        Assert.Equal(new[] { (0, 0), (1, 0) }, order);
        Assert.Equal(2, report.ChunkCount);

        // Only the two consumed chunks were sampled; the rest of the map never ran.
        Assert.Equal(2L * ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis, report.Sampling.SampleCount);
    }

    [Fact]
    public void ReportAggregatesChunkCountHeightRangeAndSamplingStats()
    {
        var grid = CreateThreeByTwoGrid();
        var elevation = CenterAlignedElevation(3, 2, southUp: false);
        var report = new TerrainGenerationReport();
        var generated = 0;

        foreach (var chunk in CreateGenerator().GenerateAll(grid, elevation, new SamplingOptions(), report))
        {
            generated++;
        }

        Assert.Equal(6, generated);
        Assert.Equal(6, report.ChunkCount);
        Assert.Equal(0, report.Sampling.FailedSampleCount);
        Assert.Equal(0, report.Sampling.NoDataFillCount);
        Assert.Equal(6L * ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis, report.Sampling.SampleCount);

        // Global samples span -1 through 3 * 128 + 1 and -1 through 2 * 128 + 1; the
        // extremes sit on the outer apron corners.
        Assert.Equal((float)PlaneHeightMetersAtSample(-1, -1), report.MinHeightGameUnits!.Value);
        Assert.Equal((float)PlaneHeightMetersAtSample((3 * 128) + 1, (2 * 128) + 1), report.MaxHeightGameUnits!.Value);
    }

    [Fact]
    public void ConstantFillEventsAreCountedWithoutDiagnosticStrings()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var elevation = CenterAlignedElevation(1, 1, southUp: true, noDataCell: (Column: 66, Row: 68));
        var diagnostics = new GeoDiagnosticBag();
        var stats = new SamplingStats();

        var chunk = CreateGenerator().Generate(
            grid!,
            elevation,
            new SamplingOptions(noData: NoDataPolicy.Constant, noDataConstantMeters: 7d),
            0,
            0,
            stats,
            diagnostics);

        Assert.NotNull(chunk);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(1, stats.NoDataFillCount);
        Assert.Equal(0, stats.FailedSampleCount);
        Assert.Equal((float)((7d - 0d) * 1d / 1d), chunk!.GetHeight(66, 68));
    }

    [Fact]
    public void ChunkIndexesAreRejectedOutsideTheGrid()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var generator = CreateGenerator();
        var elevation = CenterAlignedElevation(1, 1, southUp: false);

        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(grid!, elevation, new SamplingOptions(), -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(grid!, elevation, new SamplingOptions(), grid!.ChunksX, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(grid!, elevation, new SamplingOptions(), 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(grid!, elevation, new SamplingOptions(), 0, grid.ChunksY));
    }

    [Fact]
    public void ChunkCopiesItsHeightBufferAndValidatesInput()
    {
        var heights = BuildPlaneHeights();
        var chunk = new TerrainChunk(0, 0, heights);

        heights[0] = -12345f;
        Assert.Equal((float)PlaneHeightMetersAtSample(-1, -1), chunk.GetHeight(0, 0));
        Assert.Equal((float)PlaneHeightMetersAtSample(-1, -1), chunk.Heights.Span[0]);
        Assert.Equal((float)PlaneHeightMetersAtSample(-1, -1), chunk.MinHeightGameUnits);
        Assert.Equal((float)PlaneHeightMetersAtSample(129, 129), chunk.MaxHeightGameUnits);

        Assert.Throws<ArgumentException>(() => new TerrainChunk(0, 0, new float[100]));
        Assert.Throws<ArgumentException>(() => new TerrainChunk(0, 0, WithHeight(heights, 5, float.NaN)));
        Assert.Throws<ArgumentException>(() => new TerrainChunk(0, 0, WithHeight(heights, 5, float.PositiveInfinity)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainChunk(-1, 0, heights));
    }

    private static TerrainChunkGenerator CreateGenerator()
    {
        return new TerrainChunkGenerator(SamplingTestSupport.IdentityTransformer());
    }

    private static LocalMapGrid? CreateGrid(
        double widthMeters = 1280d,
        double heightMeters = 1280d,
        double metersPerGameUnit = 1d,
        VerticalPolicy? verticalPolicy = null)
    {
        return LocalMapGrid.Create(
            ProjectedSquare(OriginEasting, OriginNorthing, widthMeters, heightMeters),
            Tm4,
            CreateManifest(metersPerGameUnit, verticalPolicy),
            Transformer,
            new GeoDiagnosticBag());
    }

    private static LocalMapGrid CreateThreeByTwoGrid()
    {
        var grid = CreateGrid(widthMeters: 3840d, heightMeters: 2560d);
        Assert.NotNull(grid);

        return grid!;
    }

    private static GeospatialImportManifest CreateManifest(double metersPerGameUnit, VerticalPolicy? verticalPolicy)
    {
        var sources = new List<ManifestSource>
        {
            new("extent", ManifestSourceKind.Kml, "area.kml", ManifestSourceRole.Extent),
            new("dem", ManifestSourceKind.Geotiff, "dem.tif", ManifestSourceRole.Elevation, "EGM96"),
        };

        return new GeospatialImportManifest
        {
            Sources = sources,
            TargetCoordinateSystem = new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator, CentralMeridianDegrees: 4d),
            MetersPerGameUnit = metersPerGameUnit,
            DefaultMaterial = "grass",
            VerticalPolicy = verticalPolicy ?? new VerticalPolicy(),
            MaxScaleError = 0.001d,
        };
    }

    private static GeoPolygon ProjectedSquare(double west, double south, double width, double height)
    {
        return new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(new Vector2d(west, south)),
                new GeoPosition(new Vector2d(west + width, south)),
                new GeoPosition(new Vector2d(west + width, south + height)),
                new GeoPosition(new Vector2d(west, south + height)),
                new GeoPosition(new Vector2d(west, south)),
            ]));
    }

    // Dyadic coefficients keep every lattice value, bilinear weight product, and vertical
    // conversion an exact double, so expected heights are bit-reproducible.
    private static double PlaneHeightMeters(Vector2d world)
    {
        return 50d + (0.25d * (world.X - OriginEasting)) + (0.5d * (world.Y - OriginNorthing));
    }

    private static double PlaneHeightMetersAtSample(int globalX, int globalY, double spacing = 10d)
    {
        return PlaneHeightMeters(new Vector2d(OriginEasting + (spacing * globalX), OriginNorthing + (spacing * globalY)));
    }

    private static float[] BuildPlaneHeights()
    {
        var stored = ChunkLayout.StoredPointsPerChunkAxis;
        var heights = new float[stored * stored];

        for (var sy = 0; sy < stored; sy++)
        {
            for (var sx = 0; sx < stored; sx++)
            {
                heights[(sy * stored) + sx] = (float)PlaneHeightMetersAtSample(sx - 1, sy - 1);
            }
        }

        return heights;
    }

    private static float[] WithHeight(float[] heights, int index, float value)
    {
        var copy = (float[])heights.Clone();
        copy[index] = value;

        return copy;
    }

    /// <summary>
    /// A 10 m GSD raster whose cell centers sit exactly on the map lattice: column c is
    /// global sample c - 1, south-up row r is global sample r - 1, north-up row r is
    /// global sample 128 * chunksY + 1 - r. Both orientations carry the same plane.
    /// </summary>
    private static GeospatialRaster<double> CenterAlignedElevation(
        int chunksX,
        int chunksY,
        bool southUp,
        int? widthOverride = null,
        (int Column, int Row)? noDataCell = null,
        double? constant = null)
    {
        var width = widthOverride ?? (chunksX * ChunkLayout.IntervalsPerChunk) + 3;
        var height = (chunksY * ChunkLayout.IntervalsPerChunk) + 3;
        var transform = southUp
            ? SamplingTestSupport.SouthUp(OriginEasting - 15d, OriginNorthing - 15d, 10d, 10d)
            : SamplingTestSupport.NorthUp(
                OriginEasting - 15d,
                OriginNorthing + (10d * ((chunksY * ChunkLayout.IntervalsPerChunk) + 1)) + 5d,
                10d,
                10d);

        double Value(int column, int row)
        {
            if (constant is { } value)
            {
                return value;
            }

            if (noDataCell is { } cell && column == cell.Column && row == cell.Row)
            {
                return -9999d;
            }

            return PlaneHeightMeters(transform.PixelToWorld(new Vector2d(column + 0.5d, row + 0.5d)));
        }

        return SamplingTestSupport.Raster(
            width,
            height,
            transform,
            Tm4,
            Value,
            -9999d,
            noDataCell is not null);
    }

    private static int Bits(float value)
    {
        return BitConverter.SingleToInt32Bits(value);
    }
}
