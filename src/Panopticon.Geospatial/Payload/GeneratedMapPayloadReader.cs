using System;
using System.IO;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Reading;
using Panopticon.Geospatial.Payload.Serialization;
using Panopticon.Geospatial.Payload.Storage;

namespace Panopticon.Geospatial.Payload;

/// <summary>
/// Reader for one validated standalone payload directory; the full validation contract,
/// cache bounds, and lazy-read revalidation rules live in
/// <see href="../../../../docs/roadmap/implementation/track-r-r2-payload.md">track R2 payload</see>.
/// Deterministic and not thread-safe; dispose releases retained chunk arrays.
/// </summary>
public sealed class GeneratedMapPayloadReader : IGeneratedMapPayloadReader
{
    private readonly string _root;

    private readonly GeospatialImportLimits _limits;

    private readonly PayloadChunkSource _chunks;

    private readonly GeneratedMapPayloadMetadata _metadata;

    private readonly GeneratedMapPayloadReport _report;

    private readonly double _mapWidthGameUnits;

    private readonly double _mapHeightGameUnits;

    private PayloadImagerySampler? _imagerySampler;

    private TerrainChunkHeightSampler? _heightSampler;

    private GeoDiagnosticBag? _samplingDiagnostics;

    private CancellationToken _samplingCancellationToken;

    private bool _disposed;

    private GeneratedMapPayloadReader(
        string root,
        GeospatialImportLimits limits,
        GeneratedMapPayloadMetadata metadata,
        GeneratedMapPayloadReport report)
    {
        _root = root;
        _limits = limits;
        _metadata = metadata;
        _report = report;
        _chunks = new PayloadChunkSource(metadata, new PayloadChunkFileStore(root, limits), limits);
        _mapWidthGameUnits = metadata.Grid.ChunksX * (double)ChunkLayout.ChunkSpanGameUnits;
        _mapHeightGameUnits = metadata.Grid.ChunksY * (double)ChunkLayout.ChunkSpanGameUnits;
    }

    /// <summary>
    /// Opens and fully validates one payload directory. Returns null with typed
    /// diagnostics for every recoverable invalid input; cancellation propagates.
    /// </summary>
    public static GeneratedMapPayloadReader? Open(
        string directory,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedLimits = limits ?? new GeospatialImportLimits();
        var root = PayloadRootInventory.TryResolveRoot(directory, diagnostics);

        if (root is null)
        {
            return null;
        }

        var files = new PayloadChunkFileStore(root, resolvedLimits);
        var metadata = TryReadJson(
            files,
            PayloadChunkPaths.MetadataFileName,
            PayloadJson.TryReadMetadata,
            diagnostics,
            resolvedLimits,
            cancellationToken);

        if (metadata is null)
        {
            return null;
        }

        var report = TryReadJson(
            files,
            PayloadChunkPaths.ReportFileName,
            PayloadJson.TryReadReport,
            diagnostics,
            resolvedLimits,
            cancellationToken);

        if (report is null)
        {
            return null;
        }

        var payloadSha256Hex = files.TryHashBoundedJson(PayloadChunkPaths.MetadataFileName, diagnostics, cancellationToken);

        if (payloadSha256Hex is null)
        {
            return null;
        }

        if (!string.Equals(payloadSha256Hex, report.PayloadSha256Hex, StringComparison.Ordinal))
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.ReportHashMismatch,
                $"The report declares payload.json hash {report.PayloadSha256Hex} but the actual bytes hash to {payloadSha256Hex}.");

            return null;
        }

        if (!TryValidateCleanReport(report, diagnostics))
        {
            return null;
        }

        if (!PayloadImageryBudget.TryValidateGrid(metadata.Grid, resolvedLimits, diagnostics)
            || !TryValidateEncodedImageryCap(metadata, resolvedLimits, diagnostics))
        {
            return null;
        }

        if (!PayloadRootInventory.TryValidateInventory(root, metadata, diagnostics, cancellationToken))
        {
            return null;
        }

        var reader = new GeneratedMapPayloadReader(root, resolvedLimits, metadata, report);

        if (!reader._chunks.TryValidateChunks(diagnostics, cancellationToken))
        {
            reader.Dispose();

            return null;
        }

        return reader;
    }

    /// <inheritdoc />
    public GeneratedMapPayloadMetadata Metadata
    {
        get
        {
            ThrowIfDisposed();

            return _metadata;
        }
    }

    /// <inheritdoc />
    public GeneratedMapPayloadReport Report
    {
        get
        {
            ThrowIfDisposed();

            return _report;
        }
    }

    /// <inheritdoc />
    public TerrainChunk? ReadTerrain(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ChunkInRange(chunkX, chunkY, diagnostics))
        {
            return null;
        }

        return _chunks.ReadTerrain(chunkX, chunkY, diagnostics, cancellationToken);
    }

    /// <inheritdoc />
    public MaterialChunk? ReadMaterial(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ChunkInRange(chunkX, chunkY, diagnostics))
        {
            return null;
        }

        return _chunks.ReadMaterial(chunkX, chunkY, diagnostics, cancellationToken);
    }

    /// <inheritdoc />
    public TerrainMaskChunk? ReadMask(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ChunkInRange(chunkX, chunkY, diagnostics))
        {
            return null;
        }

        return _chunks.ReadMask(chunkX, chunkY, diagnostics, cancellationToken);
    }

    /// <inheritdoc />
    public ImageryChunk? ReadImagery(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();

        if (_metadata.Grid.ImageryPixelsPerSample is null)
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.ImageryAbsent,
                "The payload declares no imagery layer, so no imagery chunk can be read.");

            return null;
        }

        if (!ChunkInRange(chunkX, chunkY, diagnostics))
        {
            return null;
        }

        return _chunks.ReadImageryFile(chunkX, chunkY, diagnostics, cancellationToken);
    }

    /// <inheritdoc />
    public bool TrySampleHeight(
        double gameX,
        double gameY,
        out double gameZ,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();
        gameZ = 0d;

        if (!PositionInsideMap(gameX, gameY, diagnostics))
        {
            return false;
        }

        var sampler = _heightSampler ??= new TerrainChunkHeightSampler(
            _metadata.Grid.ChunksX,
            _metadata.Grid.ChunksY,
            ResolveTerrainForSampling);

        // The height sampler's resolver carries no diagnostic parameters; route the
        // resolver into this call's bag and token for the duration of the sample.
        _samplingDiagnostics = diagnostics;
        _samplingCancellationToken = cancellationToken;

        try
        {
            return sampler.TrySample(gameX, gameY, out gameZ);
        }
        finally
        {
            _samplingDiagnostics = null;
        }
    }

    /// <inheritdoc />
    public bool TrySampleImagery(
        double gameX,
        double gameY,
        out RgbaValue value,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();
        value = default;

        if (_metadata.Grid.ImageryPixelsPerSample is null)
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.ImageryAbsent,
                "The payload declares no imagery layer, so no imagery sample exists.");

            return false;
        }

        var sampler = _imagerySampler ??= new PayloadImagerySampler(_metadata.Grid, ResolveImageryForSampling);

        return sampler.TrySample(gameX, gameY, out value, diagnostics, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _chunks.ClearCaches();
        _heightSampler = null;
        _imagerySampler = null;
        _samplingDiagnostics = null;
    }

    /// <summary>Terrain cache occupancy for tests; never exceeds PayloadChunkSource.CacheCapacity.</summary>
    internal int TerrainCacheCountForTests => _chunks.TerrainCacheCount;

    /// <summary>Decoded imagery cache occupancy for tests; never exceeds PayloadChunkSource.CacheCapacity.</summary>
    internal int DecodedImageryCacheCountForTests => _chunks.DecodedImageryCacheCount;

    private static TModel? TryReadJson<TModel>(
        PayloadChunkFileStore files,
        string fileName,
        Func<Stream, GeoDiagnosticBag, GeospatialImportLimits, CancellationToken, TModel?> read,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits limits,
        CancellationToken cancellationToken)
        where TModel : class
    {
        using var stream = files.TryOpenRead(fileName, diagnostics, cancellationToken);

        if (stream is null)
        {
            return null;
        }

        return read(stream, diagnostics, limits, cancellationToken);
    }

    private TerrainChunk? ResolveTerrainForSampling(int chunkX, int chunkY)
    {
        return _chunks.ReadTerrain(chunkX, chunkY, _samplingDiagnostics!, _samplingCancellationToken);
    }

    private DecodedImagery? ResolveImageryForSampling(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        return _chunks.DecodeImageryCached(chunkX, chunkY, diagnostics, cancellationToken);
    }

    private bool ChunkInRange(int chunkX, int chunkY, GeoDiagnosticBag diagnostics)
    {
        if (chunkX >= 0 && chunkX < _metadata.Grid.ChunksX && chunkY >= 0 && chunkY < _metadata.Grid.ChunksY)
        {
            return true;
        }

        PayloadReaderDiagnostics.Error(
            diagnostics,
            PayloadReaderDiagnosticCodes.ChunkCoordinatesOutOfRange,
            $"The chunk coordinates ({chunkX}, {chunkY}) lie outside the {_metadata.Grid.ChunksX} x {_metadata.Grid.ChunksY} chunk grid.");

        return false;
    }

    private bool PositionInsideMap(double gameX, double gameY, GeoDiagnosticBag diagnostics)
    {
        if (gameX >= 0d && gameX <= _mapWidthGameUnits && gameY >= 0d && gameY <= _mapHeightGameUnits)
        {
            return true;
        }

        PayloadReaderDiagnostics.Error(
            diagnostics,
            PayloadReaderDiagnosticCodes.SampleOutsideMap,
            $"The sample position ({gameX}, {gameY}) lies outside the map rectangle [0, {_mapWidthGameUnits}] x [0, {_mapHeightGameUnits}].");

        return false;
    }

    private static bool TryValidateCleanReport(GeneratedMapPayloadReport report, GeoDiagnosticBag diagnostics)
    {
        foreach (var diagnostic in report.Diagnostics)
        {
            if (diagnostic.Severity == GeoSeverity.Error)
            {
                PayloadReaderDiagnostics.Error(
                    diagnostics,
                    PayloadReaderDiagnosticCodes.ReportNotClean,
                    $"The report carries the error diagnostic '{diagnostic.Code}': {diagnostic.Message}");

                return false;
            }
        }

        foreach (var check in report.ValidationChecks)
        {
            if (check.Status == PayloadValidationStatus.Failed)
            {
                PayloadReaderDiagnostics.Error(
                    diagnostics,
                    PayloadReaderDiagnosticCodes.ReportNotClean,
                    $"The report carries the failed validation check '{check.Category}/{check.Name}'.");

                return false;
            }
        }

        return true;
    }

    /// <summary>Rejects any imagery descriptor whose encoded length exceeds the encoded overhead cap before a file is read.</summary>
    private static bool TryValidateEncodedImageryCap(GeneratedMapPayloadMetadata metadata, GeospatialImportLimits limits, GeoDiagnosticBag diagnostics)
    {
        if (metadata.Grid.ImageryPixelsPerSample is null)
        {
            return true;
        }

        var cap = PayloadImageryBudget.EncodedPngCapBytes(limits);

        foreach (var chunk in metadata.Chunks)
        {
            if (chunk.Imagery!.LengthBytes > cap)
            {
                PayloadReaderDiagnostics.Error(
                    diagnostics,
                    PayloadReaderDiagnosticCodes.ImageryBudgetExceeded,
                    $"The imagery file '{chunk.Imagery.Path}' declares {chunk.Imagery.LengthBytes} encoded bytes, beyond the {cap}-byte encoded cap.");

                return false;
            }
        }

        return true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
