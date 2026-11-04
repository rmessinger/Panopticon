#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Projection;
using Panopticon.Geospatial.Sampling;
using Panopticon.Geospatial.Tests.Sampling;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Panopticon.Geospatial.Tests.Generation;

public class ImageryChunkGeneratorTests
{
    private const double OriginEasting = 100000d;

    private const double OriginNorthing = 6000000d;

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    private static readonly CoordinateSystemId Tm4 = CoordinateSystemId.TransverseMercator(4d, 1d, 0d, 0d);

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void ControlPixelsMatchTheResampledSourceAndTheHeaderIsPinnedRgba8()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var chunk = CreateGenerator().Generate(
            grid!,
            LatticeImagery(bandCount: 3, southUp: true),
            new SamplingOptions(),
            0,
            0,
            imageryPixelsPerSample: 1);

        Assert.NotNull(chunk);
        Assert.Equal(128, chunk!.PixelsPerAxis);
        AssertPngHeader(chunk, 128);

        // One-to-one footprints select bilinear; each pixel center sits exactly between
        // two source cells per axis, so every channel is a dyadic average. R rising east
        // and G rising north pin the orientation, and the x.5 averages pin the
        // round-half-away-from-zero byte rule.
        var pixels = DecodePixels(chunk);

        for (var py = 0; py < 128; py++)
        {
            for (var px = 0; px < 128; px++)
            {
                var pixel = pixels[(py * 128) + px];

                Assert.Equal(px + 1, pixel.R);
                Assert.Equal(128 - py, pixel.G);
                Assert.Equal(77, pixel.B);
                Assert.Equal(255, pixel.A);
            }
        }
    }

    [Fact]
    public void FourBandSourcesStoreTheStraightAlphaChannel()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var chunk = CreateGenerator().Generate(
            grid!,
            LatticeImagery(bandCount: 4, southUp: true),
            new SamplingOptions(),
            0,
            0,
            imageryPixelsPerSample: 1);

        Assert.NotNull(chunk);

        // Alpha 200 everywhere: RGB stays identical to the opaque 3-band output, so the
        // alpha is stored straight rather than premultiplied.
        var pixels = DecodePixels(chunk!);

        for (var py = 0; py < 128; py++)
        {
            for (var px = 0; px < 128; px++)
            {
                var pixel = pixels[(py * 128) + px];

                Assert.Equal(px + 1, pixel.R);
                Assert.Equal(128 - py, pixel.G);
                Assert.Equal(77, pixel.B);
                Assert.Equal(200, pixel.A);
            }
        }
    }

    [Fact]
    public void NorthUpAndSouthUpSourcesProduceIdenticalChunks()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var generator = CreateGenerator();
        var north = generator.Generate(grid!, LatticeImagery(3, southUp: false), new SamplingOptions(), 0, 0, imageryPixelsPerSample: 1);
        var south = generator.Generate(grid!, LatticeImagery(3, southUp: true), new SamplingOptions(), 0, 0, imageryPixelsPerSample: 1);

        Assert.NotNull(north);
        Assert.NotNull(south);
        Assert.Equal(north!.Png.ToArray(), south!.Png.ToArray());
        Assert.Equal(north.PngSha256Hex, south.PngSha256Hex);

        // The gradient distinguishes every row and column, so a raster that was mirrored
        // instead of flipped would fail the per-pixel expectations.
        var pixels = DecodePixels(north);
        Assert.Equal(1, pixels[0].R);
        Assert.Equal(128, pixels[0].G);
        Assert.Equal(128, pixels[127].R);
        Assert.Equal(1, pixels[(127 * 128) + 127].G);
    }

    [Fact]
    public void DecimatedFootprintsAreaAverageTheCoveredSourceCells()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var chunk = CreateGenerator().Generate(
            grid!,
            DecimationImagery(),
            new SamplingOptions(),
            0,
            0,
            imageryPixelsPerSample: 1);

        Assert.NotNull(chunk);

        // A 5 m source against 10 m output pixels decimates, so Auto selects area
        // weighting. Each footprint covers three source cells per axis with weights
        // 0.5, 1, 0.5 centered on cell 2*index + 1; a center-only kernel would return
        // just the center cell and miss these values.
        var pixels = DecodePixels(chunk!);

        for (var py = 0; py < 128; py++)
        {
            for (var px = 0; px < 128; px++)
            {
                var pixel = pixels[(py * 128) + px];

                Assert.Equal(ExpectedAreaRed(px), pixel.R);
                Assert.Equal(ExpectedAreaGreen(py), pixel.G);
                Assert.Equal(77, pixel.B);
                Assert.Equal(255, pixel.A);
            }
        }
    }

    [Fact]
    public void ConstantUint8ImageryDecimatesWithoutOvershootingTheChannelRange()
    {
        // 0.3048 m output pixels over 0.2 m source cells decimate, so Auto area-weights
        // each footprint; a constant source must still yield exactly 255 in every channel.
        const double LargeOriginEasting = 639837d;
        const double LargeOriginNorthing = 5507944d;
        const double SourceGsd = 0.2d;
        const int SourceCells = 220;
        const double MetersPerGameUnit = 0.03048d;

        var chunkSpanMeters = ChunkLayout.IntervalsPerChunk * ChunkLayout.SampleSpacingGameUnits * MetersPerGameUnit;
        var gridDiagnostics = new GeoDiagnosticBag();
        var manifest = CreateManifest(MetersPerGameUnit) with
        {
            TargetCoordinateSystem = new TargetCoordinateSystemSpec(ManifestCrsKind.Utm, 31, ManifestHemisphere.North),
        };
        var grid = LocalMapGrid.Create(
            ProjectedSquare(LargeOriginEasting, LargeOriginNorthing, chunkSpanMeters, chunkSpanMeters),
            SamplingTestSupport.Utm31,
            manifest,
            Transformer,
            gridDiagnostics);
        Assert.True(grid is not null, string.Join("; ", gridDiagnostics.Items.Select(d => d.Message)));

        var samples = new double[SourceCells * SourceCells * 3];
        Array.Fill(samples, 255d);
        var imagery = new GeospatialRaster<double>(
            SourceCells,
            SourceCells,
            3,
            SamplingTestSupport.NorthUp(LargeOriginEasting - 1d, LargeOriginNorthing + 41d, SourceGsd, SourceGsd),
            grid!.ProjectedCoordinateSystem,
            samples,
            sampleFormat: RasterSampleFormat.UInt8);

        var diagnostics = new GeoDiagnosticBag();
        var chunk = CreateGenerator().Generate(
            grid!,
            imagery,
            new SamplingOptions(),
            0,
            0,
            imageryPixelsPerSample: 1,
            diagnostics: diagnostics);

        Assert.True(chunk is not null, string.Join("; ", diagnostics.Items.Select(d => d.Message)));
        Assert.Empty(diagnostics.Items);

        foreach (var pixel in DecodePixels(chunk!))
        {
            Assert.Equal(new Rgba32(255, 255, 255, 255), pixel);
        }
    }

    [Fact]
    public void TwoRunsProduceIdenticalPngBytesAndIndependentHashes()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var imagery = LatticeImagery(3, southUp: false);
        var first = CreateGenerator().Generate(grid!, imagery, new SamplingOptions(), 0, 0);
        var second = new ImageryChunkGenerator(SamplingTestSupport.IdentityTransformer())
            .Generate(grid!, imagery, new SamplingOptions(), 0, 0);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Png.ToArray(), second!.Png.ToArray());
        Assert.Equal(first.PngSha256Hex, second.PngSha256Hex);
        AssertPngHeader(first, 1280);

        var expected = Convert.ToHexString(SHA256.HashData(first.Png.ToArray())).ToLowerInvariant();
        Assert.Equal(expected, first.PngSha256Hex);
    }

    [Fact]
    public void PixelsPerSampleBoundsAreTypedErrors()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var generator = CreateGenerator();
        var imagery = LatticeImagery(3, southUp: true);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => generator.Generate(grid!, imagery, new SamplingOptions(), 0, 0, imageryPixelsPerSample: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => generator.Generate(grid!, imagery, new SamplingOptions(), 0, 0, imageryPixelsPerSample: -3));

        AssertRejectsWithGenerateFailure(generator, grid!, imagery, int.MaxValue);
        AssertRejectsWithGenerateFailure(generator, grid!, imagery, 600);

        var limits = new GeospatialImportLimits { MaxRasterDecodedBytes = 1024 * 1024 };
        AssertRejectsWithGenerateFailure(generator, grid!, imagery, 10, limits);
    }

    [Fact]
    public void GlobalPixelIndexOverflowIsRejectedBeforeAnyAllocation()
    {
        // metersPerGameUnit 0.0004 shrinks one chunk span to 0.512 m, so a 136,381 m
        // extent spans 266,370 chunks. Chunk 266,305 at 63 pixels per sample starts at
        // global column 2,147,483,520 (127 below int.MaxValue); adding the final column
        // overflows it. The tiny allocation budget would also reject the chunk, so the
        // single index-overflow diagnostic proves the index extent is checked before any
        // allocation.
        var grid = LocalMapGrid.Create(
            ProjectedSquare(-68190.5d, OriginNorthing, 136381d, 0.512d),
            Tm4,
            CreateManifest(metersPerGameUnit: 0.0004d),
            Transformer,
            new GeoDiagnosticBag(),
            new GeospatialImportLimits { MaxChunkCount = 1_000_000 });
        Assert.NotNull(grid);
        Assert.Equal(266_370, grid!.ChunksX);

        var diagnostics = new GeoDiagnosticBag();
        var chunk = CreateGenerator().Generate(
            grid,
            LatticeImagery(3, southUp: true),
            new SamplingOptions(),
            266_305,
            0,
            imageryPixelsPerSample: 63,
            diagnostics: diagnostics,
            limits: new GeospatialImportLimits { MaxRasterDecodedBytes = 1024 });

        Assert.Null(chunk);

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(GeoDiagnosticCodes.GenerateFailure, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.Contains("global output pixel indices", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompatibleRasterLayoutsAreRejectedWithRasterErrors()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var generator = CreateGenerator();

        foreach (var bandCount in new[] { 1, 2, 5 })
        {
            AssertRejectsWithRasterFailure(generator, grid!, LatticeImagery(bandCount, southUp: true));
        }

        AssertRejectsWithRasterFailure(generator, grid!, LatticeImagery(3, southUp: true, sampleFormat: RasterSampleFormat.Float32));
        AssertRejectsWithRasterFailure(generator, grid!, LatticeImagery(3, southUp: true, sampleFormat: RasterSampleFormat.UInt16));
    }

    [Fact]
    public void InvalidUint8PayloadsAreRejectedWithTypedErrors()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var outOfRange = CreateGenerator().Generate(
            grid!,
            LatticeImagery(3, southUp: true, bandOneOverride: 300d),
            new SamplingOptions(),
            0,
            0,
            imageryPixelsPerSample: 1);

        Assert.Null(outOfRange);

        var generator = CreateGenerator();
        var diagnostics = new GeoDiagnosticBag();
        generator.Generate(
            grid!,
            LatticeImagery(3, southUp: true, bandOneOverride: 300d),
            new SamplingOptions(),
            0,
            0,
            imageryPixelsPerSample: 1,
            diagnostics: diagnostics);

        Assert.Contains(
            diagnostics.Items,
            d => d.Code == GeoDiagnosticCodes.RasterFailure
                && d.Stage == GeoStage.Raster
                && d.Severity == GeoSeverity.Error
                && d.Message.Contains("0..255", StringComparison.Ordinal));

        // Non-finite stored values are unusable samples, so the fail no-data policy
        // rejects them through the sampler path.
        var nonFinite = new GeoDiagnosticBag();
        var chunk = CreateGenerator().Generate(
            grid!,
            LatticeImagery(3, southUp: true, bandOneOverride: double.NaN),
            new SamplingOptions(),
            0,
            0,
            imageryPixelsPerSample: 1,
            diagnostics: nonFinite);

        Assert.Null(chunk);
        Assert.Contains(nonFinite.Items, d => d.Code == SamplingDiagnosticCodes.NoDataPolicyFailure);
        Assert.Contains(nonFinite.Items, d => d.Code == GeoDiagnosticCodes.GenerateFailure);
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
                LatticeImagery(3, southUp: true),
                new SamplingOptions(),
                0,
                0,
                imageryPixelsPerSample: 1,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void CancellationDuringStreamingStopsBeforeTheNextChunk()
    {
        var grid = CreateGrid(widthMeters: 2560d);
        Assert.NotNull(grid);

        using var cancellation = new CancellationTokenSource();

        var enumerator = CreateGenerator()
            .GenerateAll(
                grid!,
                LatticeImagery(3, southUp: true),
                new SamplingOptions(),
                imageryPixelsPerSample: 1,
                cancellationToken: cancellation.Token)
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
    public void GenerateAllStreamsSouthwestRowMajorWithGlobalPixelGeometry()
    {
        var grid = CreateGrid(widthMeters: 2560d);
        Assert.NotNull(grid);
        Assert.Equal(2, grid!.ChunksX);

        var order = new List<(int X, int Y)>();
        var generator = CreateGenerator();
        var imagery = LatticeImagery(3, southUp: true, chunksX: 2);

        foreach (var chunk in generator.GenerateAll(grid, imagery, new SamplingOptions(), imageryPixelsPerSample: 1))
        {
            order.Add((chunk.ChunkX, chunk.ChunkY));
        }

        Assert.Equal([(0, 0), (1, 0)], order);

        var west = generator.Generate(grid, imagery, new SamplingOptions(), 0, 0, imageryPixelsPerSample: 1);
        var east = generator.Generate(grid, imagery, new SamplingOptions(), 1, 0, imageryPixelsPerSample: 1);

        Assert.NotNull(west);
        Assert.NotNull(east);

        // Chunk 0's easternmost pixel averages global cells 127 and 128; chunk 1's
        // westernmost averages 128 and 129. The step proves chunk 1 builds on the global
        // pixel index, not a chunk-local recomputation.
        var westPixels = DecodePixels(west!);
        var eastPixels = DecodePixels(east!);

        Assert.Equal(128, westPixels[127].R);
        Assert.Equal(129, eastPixels[0].R);

        // Row geometry is chunk-local and identical in both chunks.
        Assert.Equal(128, eastPixels[0].G);
        Assert.Equal(1, eastPixels[(127 * 128) + 0].G);
    }

    [Fact]
    public void ConstantFillEventsAreCountedOnStatsWithoutDiagnostics()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var stats = new SamplingStats();
        var diagnostics = new GeoDiagnosticBag();
        var chunk = CreateGenerator().Generate(
            grid!,
            LatticeImagery(3, southUp: true, noDataCell: (Column: 66, Row: 68)),
            new SamplingOptions(noData: NoDataPolicy.Constant, noDataConstantMeters: 7d),
            0,
            0,
            imageryPixelsPerSample: 1,
            stats,
            diagnostics);

        Assert.NotNull(chunk);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(0, stats.FailedSampleCount);
        Assert.Equal(128L * 128 * 3, stats.SampleCount);
        Assert.True(stats.NoDataFillCount > 0);

        // The filled cell (66, 68) is one of the four bilinear neighbours of output
        // pixel (64, 60), so each channel blends 7 with its three untouched neighbours.
        var pixels = DecodePixels(chunk!);
        var pixel = pixels[(60 * 128) + 64];

        Assert.Equal(50, pixel.R);
        Assert.Equal(53, pixel.G);
        Assert.Equal(60, pixel.B);
    }

    [Fact]
    public void ChunkIndexesAreRejectedOutsideTheGrid()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var generator = CreateGenerator();
        var imagery = LatticeImagery(3, southUp: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(grid!, imagery, new SamplingOptions(), -1, 0, imageryPixelsPerSample: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(grid!, imagery, new SamplingOptions(), grid!.ChunksX, 0, imageryPixelsPerSample: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(grid!, imagery, new SamplingOptions(), 0, -1, imageryPixelsPerSample: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(grid!, imagery, new SamplingOptions(), 0, grid.ChunksY, imageryPixelsPerSample: 1));
    }

    [Fact]
    public void ImageryChunkValidatesAndCopiesItsPayload()
    {
        var payload = new byte[PngSignature.Length + 4];
        PngSignature.CopyTo(payload, 0);
        payload[^1] = 42;

        var chunk = new ImageryChunk(3, 7, 64, payload);

        payload[^1] = 255;

        Assert.Equal(3, chunk.ChunkX);
        Assert.Equal(7, chunk.ChunkY);
        Assert.Equal(64, chunk.PixelsPerAxis);
        Assert.Equal(42, chunk.Png.Span[^1]);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(chunk.Png.ToArray())).ToLowerInvariant(),
            chunk.PngSha256Hex);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageryChunk(-1, 0, 64, payload));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageryChunk(0, -1, 64, payload));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageryChunk(0, 0, 0, payload));
        Assert.Throws<ArgumentException>(() => new ImageryChunk(0, 0, 64, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
    }

    private static ImageryChunkGenerator CreateGenerator()
    {
        return new ImageryChunkGenerator(SamplingTestSupport.IdentityTransformer());
    }

    private static LocalMapGrid? CreateGrid(double widthMeters = 1280d, double heightMeters = 1280d)
    {
        return LocalMapGrid.Create(
            ProjectedSquare(OriginEasting, OriginNorthing, widthMeters, heightMeters),
            Tm4,
            CreateManifest(),
            Transformer,
            new GeoDiagnosticBag());
    }

    private static GeospatialImportManifest CreateManifest(double metersPerGameUnit = 1d)
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
            VerticalPolicy = new VerticalPolicy(),
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

    /// <summary>
    /// A uint8 RGB(A) raster at 10 m GSD sized to cover chunksX x chunksY chunks, whose
    /// cell centers sit exactly on the map lattice: column c is global sample c - 1
    /// (south-up); north-up flips rows. Channels: R = global x, G = global y, B = 77,
    /// A = 200; dyadic integer values keep every expectation exact.
    /// </summary>
    private static GeospatialRaster<double> LatticeImagery(
        int bandCount,
        bool southUp,
        int chunksX = 1,
        int chunksY = 1,
        double? bandOneOverride = null,
        RasterSampleFormat sampleFormat = RasterSampleFormat.UInt8,
        (int Column, int Row)? noDataCell = null)
    {
        var cellsX = (chunksX * ChunkLayout.IntervalsPerChunk) + 3;
        var cellsY = (chunksY * ChunkLayout.IntervalsPerChunk) + 3;
        var transform = southUp
            ? SamplingTestSupport.SouthUp(OriginEasting - 15d, OriginNorthing - 15d, 10d, 10d)
            : SamplingTestSupport.NorthUp(
                OriginEasting - 15d,
                OriginNorthing + (10d * ((chunksY * ChunkLayout.IntervalsPerChunk) + 1)) + 5d,
                10d,
                10d);

        var samples = new double[cellsX * cellsY * bandCount];

        for (var row = 0; row < cellsY; row++)
        {
            for (var column = 0; column < cellsX; column++)
            {
                var world = transform.PixelToWorld(new Vector2d(column + 0.5d, row + 0.5d));
                var globalX = (int)((world.X - OriginEasting) / 10d);
                var globalY = (int)((world.Y - OriginNorthing) / 10d);

                for (var band = 0; band < bandCount; band++)
                {
                    double value;

                    if (band == 1 && bandOneOverride is { } forced)
                    {
                        value = forced;
                    }
                    else
                    {
                        if (noDataCell is { } cell && column == cell.Column && row == cell.Row)
                        {
                            value = -9999d;
                        }
                        else
                        {
                            value = band switch
                            {
                                0 => Math.Min(globalX, 255),
                                1 => Math.Min(globalY, 255),
                                2 => 77d,
                                _ => 200d,
                            };
                        }
                    }

                    samples[((row * cellsX) + column) * bandCount + band] = value;
                }
            }
        }

        return new GeospatialRaster<double>(
            cellsX,
            cellsY,
            bandCount,
            transform,
            Tm4,
            samples,
            -9999d,
            noDataCell is not null,
            sampleFormat);
    }

    /// <summary>
    /// A 257 x 257 uint8 RGB raster at 5 m GSD whose cell centers sit at multiples of
    /// 5 m from the origin, so each 10 m output pixel decimates: its Auto footprint
    /// covers source cells 2*index through 2*index + 2 per axis with area weights
    /// 0.5, 1, 0.5, centered exactly on cell 2*index + 1.
    /// </summary>
    private static GeospatialRaster<double> DecimationImagery()
    {
        const int Cells = 257;
        var transform = SamplingTestSupport.SouthUp(OriginEasting - 2.5d, OriginNorthing - 2.5d, 5d, 5d);

        var samples = new double[Cells * Cells * 3];

        for (var row = 0; row < Cells; row++)
        {
            for (var column = 0; column < Cells; column++)
            {
                samples[((row * Cells) + column) * 3 + 0] = AreaRed(column);
                samples[((row * Cells) + column) * 3 + 1] = AreaGreen(row);
                samples[((row * Cells) + column) * 3 + 2] = 77d;
            }
        }

        return new GeospatialRaster<double>(
            Cells,
            Cells,
            3,
            transform,
            Tm4,
            samples,
            sampleFormat: RasterSampleFormat.UInt8);
    }

    private static int AreaRed(int cell) => 2 * (cell % 128);

    private static int AreaGreen(int cell) => 2 * ((cell + 64) % 128);

    private static byte ExpectedAreaRed(int px)
    {
        var value = (0.5 * AreaRed(2 * px)) + AreaRed(2 * px + 1) + (0.5 * AreaRed(2 * px + 2));

        return ToByte(value / 2d);
    }

    private static byte ExpectedAreaGreen(int py)
    {
        var centerRow = 255 - (2 * py);
        var value = (0.5 * AreaGreen(centerRow - 1)) + AreaGreen(centerRow) + (0.5 * AreaGreen(centerRow + 1));

        return ToByte(value / 2d);
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Round(Math.Clamp(value, 0d, 255d), MidpointRounding.AwayFromZero);
    }

    private static Rgba32[] DecodePixels(ImageryChunk chunk)
    {
        using var image = Image.Load<Rgba32>(chunk.Png.Span);

        var pixels = new Rgba32[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);

        return pixels;
    }

    /// <summary>Asserts the fixed encoder output independently of decoding: RGBA8, no palette, no interlace.</summary>
    private static void AssertPngHeader(ImageryChunk chunk, int expectedPixelsPerAxis)
    {
        var png = chunk.Png.Span;

        Assert.True(png.Length >= 33);
        Assert.Equal(PngSignature, png[..8].ToArray());
        Assert.Equal("IHDR"u8.ToArray(), png[12..16].ToArray());
        Assert.Equal(expectedPixelsPerAxis, BinaryPrimitives.ReadInt32BigEndian(png[16..20]));
        Assert.Equal(expectedPixelsPerAxis, BinaryPrimitives.ReadInt32BigEndian(png[20..24]));
        Assert.Equal((byte)8, png[24]);
        Assert.Equal((byte)6, png[25]);
        Assert.Equal((byte)0, png[26]);
        Assert.Equal((byte)0, png[27]);
        Assert.Equal((byte)0, png[28]);
    }

    private static void AssertRejectsWithGenerateFailure(
        ImageryChunkGenerator generator,
        LocalMapGrid grid,
        GeospatialRaster<double> imagery,
        int pixelsPerSample,
        GeospatialImportLimits? limits = null)
    {
        var diagnostics = new GeoDiagnosticBag();
        var chunk = generator.Generate(grid, imagery, new SamplingOptions(), 0, 0, pixelsPerSample, diagnostics: diagnostics, limits: limits);

        Assert.Null(chunk);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(
            diagnostics.Items,
            d => d.Code == GeoDiagnosticCodes.GenerateFailure
                && d.Stage == GeoStage.Generate
                && d.Severity == GeoSeverity.Error);
    }

    private static void AssertRejectsWithRasterFailure(
        ImageryChunkGenerator generator,
        LocalMapGrid grid,
        GeospatialRaster<double> imagery)
    {
        var diagnostics = new GeoDiagnosticBag();
        var chunk = generator.Generate(grid, imagery, new SamplingOptions(), 0, 0, imageryPixelsPerSample: 1, diagnostics: diagnostics);

        Assert.Null(chunk);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(
            diagnostics.Items,
            d => d.Code == GeoDiagnosticCodes.RasterFailure
                && d.Stage == GeoStage.Raster
                && d.Severity == GeoSeverity.Error);
    }
}
