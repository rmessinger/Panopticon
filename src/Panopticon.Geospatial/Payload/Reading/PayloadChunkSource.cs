using System;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Storage;

namespace Panopticon.Geospatial.Payload.Reading;

/// <summary>
/// Chunk-level access for one opened payload: verified lazy reads through the accepted
/// <see cref="PayloadChunkCodec"/>, the bounded terrain and decoded-imagery caches, and
/// the sequential open-time validation pass. Never retains all chunks; not thread-safe.
/// </summary>
internal sealed class PayloadChunkSource
{
    /// <summary>Maximum terrain and decoded-imagery chunks retained per reader.</summary>
    internal const int CacheCapacity = 4;

    private readonly GeneratedMapPayloadMetadata _metadata;

    private readonly PayloadChunkFileStore _files;

    private readonly GeospatialImportLimits _limits;

    private readonly PayloadReaderCache<(int ChunkX, int ChunkY), TerrainChunk> _terrainCache = new(CacheCapacity);

    private readonly PayloadReaderCache<(int ChunkX, int ChunkY), DecodedImagery> _decodedImageryCache = new(CacheCapacity);

    internal PayloadChunkSource(GeneratedMapPayloadMetadata metadata, PayloadChunkFileStore files, GeospatialImportLimits limits)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(limits);

        _metadata = metadata;
        _files = files;
        _limits = limits;
    }

    internal int TerrainCacheCount => _terrainCache.Count;

    internal int DecodedImageryCacheCount => _decodedImageryCache.Count;

    /// <summary>
    /// Reads one terrain chunk through the bounded cache; a cache hit re-streams the file
    /// to reverify length and hash, so later mutation fails closed instead of returning
    /// a stale chunk.
    /// </summary>
    internal TerrainChunk? ReadTerrain(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        var reference = _metadata.Chunks[(chunkY * _metadata.Grid.ChunksX) + chunkX].Terrain;

        if (_terrainCache.TryGet((chunkX, chunkY), out var cached))
        {
            if (!_files.TryVerify(reference.Path, reference.LengthBytes, reference.Sha256Hex, diagnostics, cancellationToken))
            {
                _terrainCache.Remove((chunkX, chunkY));

                return null;
            }

            return cached;
        }

        var encoded = _files.TryReadVerified(reference.Path, reference.LengthBytes, reference.Sha256Hex, diagnostics, cancellationToken);

        if (encoded is null)
        {
            return null;
        }

        var chunk = PayloadChunkCodec.DecodeTerrain(chunkX, chunkY, encoded, diagnostics, cancellationToken);

        if (chunk is null)
        {
            return null;
        }

        _terrainCache.Store((chunkX, chunkY), chunk);

        return chunk;
    }

    /// <summary>Reads one material chunk; no material chunks are retained between calls.</summary>
    internal MaterialChunk? ReadMaterial(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        var reference = _metadata.Chunks[(chunkY * _metadata.Grid.ChunksX) + chunkX].Materials;
        var encoded = _files.TryReadVerified(reference.Path, reference.LengthBytes, reference.Sha256Hex, diagnostics, cancellationToken);

        if (encoded is null)
        {
            return null;
        }

        return PayloadChunkCodec.DecodeMaterial(chunkX, chunkY, encoded, _metadata.Palette, diagnostics, cancellationToken);
    }

    /// <summary>Reads one mask chunk; no mask chunks are retained between calls.</summary>
    internal TerrainMaskChunk? ReadMask(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        var reference = _metadata.Chunks[(chunkY * _metadata.Grid.ChunksX) + chunkX].Masks;
        var encoded = _files.TryReadVerified(reference.Path, reference.LengthBytes, reference.Sha256Hex, diagnostics, cancellationToken);

        if (encoded is null)
        {
            return null;
        }

        return PayloadChunkCodec.DecodeMask(chunkX, chunkY, encoded, diagnostics, cancellationToken);
    }

    /// <summary>Reads one imagery chunk's encoded PNG bytes without decoding it.</summary>
    internal ImageryChunk? ReadImageryFile(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        if (_metadata.Grid.ImageryPixelsPerSample is not { } pixelsPerSample)
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.ImageryAbsent,
                $"The payload declares no imagery layer, so chunk ({chunkX}, {chunkY}) has no imagery to read.");

            return null;
        }

        var reference = _metadata.Chunks[(chunkY * _metadata.Grid.ChunksX) + chunkX].Imagery!;
        var encoded = _files.TryReadVerified(reference.Path, reference.LengthBytes, reference.Sha256Hex, diagnostics, cancellationToken);

        if (encoded is null)
        {
            return null;
        }

        return new ImageryChunk(chunkX, chunkY, checked(ChunkLayout.IntervalsPerChunk * pixelsPerSample), encoded);
    }

    /// <summary>
    /// Decodes one imagery chunk through the bounded decoded cache; a cache hit
    /// re-streams the PNG to reverify length and hash before the pixels are reused.
    /// </summary>
    internal DecodedImagery? DecodeImageryCached(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        if (_metadata.Grid.ImageryPixelsPerSample is not { } pixelsPerSample)
        {
            throw new InvalidOperationException("The payload declares no imagery layer.");
        }

        var reference = _metadata.Chunks[(chunkY * _metadata.Grid.ChunksX) + chunkX].Imagery!;

        if (_decodedImageryCache.TryGet((chunkX, chunkY), out var cached))
        {
            if (!_files.TryVerify(reference.Path, reference.LengthBytes, reference.Sha256Hex, diagnostics, cancellationToken))
            {
                _decodedImageryCache.Remove((chunkX, chunkY));

                return null;
            }

            return cached;
        }

        var encoded = _files.TryReadVerified(reference.Path, reference.LengthBytes, reference.Sha256Hex, diagnostics, cancellationToken);

        if (encoded is null)
        {
            return null;
        }

        var chunk = new ImageryChunk(chunkX, chunkY, checked(ChunkLayout.IntervalsPerChunk * pixelsPerSample), encoded);
        var decoded = PayloadChunkCodec.DecodeImagery(chunk, chunk.PixelsPerAxis, _limits, diagnostics, cancellationToken);

        if (decoded is null)
        {
            return null;
        }

        _decodedImageryCache.Store((chunkX, chunkY), decoded);

        return decoded;
    }

    internal void ClearCaches()
    {
        _terrainCache.Clear();
        _decodedImageryCache.Clear();
    }

    /// <summary>
    /// Sequential open-time pass over every chunk: terrain, material, and mask files are
    /// verified, decoded, and seam-compared row-major with bounded neighbour rereads,
    /// then each imagery PNG is fully validated through the codec. Returns false with a
    /// typed diagnostic on the first failure.
    /// </summary>
    internal bool TryValidateChunks(GeoDiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        var grid = _metadata.Grid;

        for (var chunkY = 0; chunkY < grid.ChunksY; chunkY++)
        {
            for (var chunkX = 0; chunkX < grid.ChunksX; chunkX++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var terrain = ReadTerrain(chunkX, chunkY, diagnostics, cancellationToken);

                if (terrain is null)
                {
                    return false;
                }

                if (chunkX > 0)
                {
                    var west = ReadTerrain(chunkX - 1, chunkY, diagnostics, cancellationToken);

                    if (west is null
                        || !PayloadChunkCodec.ValidateTerrainHorizontalSeam(west, terrain, diagnostics))
                    {
                        return false;
                    }
                }

                if (chunkY > 0)
                {
                    var south = ReadTerrain(chunkX, chunkY - 1, diagnostics, cancellationToken);

                    if (south is null
                        || !PayloadChunkCodec.ValidateTerrainVerticalSeam(south, terrain, diagnostics))
                    {
                        return false;
                    }
                }
            }
        }

        if (!TryValidateCoreSeams(ReadMaterial, PayloadChunkCodec.ValidateMaterialHorizontalSeam, PayloadChunkCodec.ValidateMaterialVerticalSeam, diagnostics, cancellationToken)
            || !TryValidateCoreSeams(ReadMask, PayloadChunkCodec.ValidateMaskHorizontalSeam, PayloadChunkCodec.ValidateMaskVerticalSeam, diagnostics, cancellationToken))
        {
            return false;
        }

        if (grid.ImageryPixelsPerSample is null)
        {
            return true;
        }

        foreach (var chunk in _metadata.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DecodeImageryCached(chunk.ChunkX, chunk.ChunkY, diagnostics, cancellationToken) is null)
            {
                return false;
            }
        }

        return true;
    }

    private delegate TChunk? ChunkReader<TChunk>(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken)
        where TChunk : class;

    private delegate bool SeamValidator<in TChunk>(TChunk previous, TChunk next, GeoDiagnosticBag? diagnostics = null);

    /// <summary>
    /// Material and mask pass: each chunk is read once for itself, the western neighbour
    /// is retained in a single slot, and the southern neighbour is reread on demand, so
    /// no more than three chunks of one layer are ever live.
    /// </summary>
    private bool TryValidateCoreSeams<TChunk>(
        ChunkReader<TChunk> reader,
        SeamValidator<TChunk> horizontalSeam,
        SeamValidator<TChunk> verticalSeam,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
        where TChunk : class
    {
        var grid = _metadata.Grid;

        for (var chunkY = 0; chunkY < grid.ChunksY; chunkY++)
        {
            TChunk? west = null;

            for (var chunkX = 0; chunkX < grid.ChunksX; chunkX++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var current = reader(chunkX, chunkY, diagnostics, cancellationToken);

                if (current is null)
                {
                    return false;
                }

                if (west is not null && !horizontalSeam(west, current, diagnostics))
                {
                    return false;
                }

                if (chunkY > 0)
                {
                    var south = reader(chunkX, chunkY - 1, diagnostics, cancellationToken);

                    if (south is null || !verticalSeam(south, current, diagnostics))
                    {
                        return false;
                    }
                }

                west = current;
            }
        }

        return true;
    }
}
