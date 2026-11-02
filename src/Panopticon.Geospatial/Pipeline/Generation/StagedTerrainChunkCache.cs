using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Storage;

namespace Panopticon.Geospatial.Pipeline.Generation;

/// <summary>
/// Read-only cache over the staged terrain files (R2 staged-write contract): every
/// resolve re-verifies the canonical path, links, the fixed byte length, and the
/// SHA-256 against the written reference before any cached chunk is returned. At most
/// <see cref="Capacity"/> decoded chunks are retained; constructed per run.
/// </summary>
internal sealed class StagedTerrainChunkCache
{
    /// <summary>Terrain and decoded-imagery caches each retain at most four chunks.</summary>
    public const int Capacity = 4;

    private readonly string _stageDirectory;

    private readonly int _chunksX;

    private readonly int _chunksY;

    private readonly PayloadFileReference[] _terrainReferences;

    private readonly GeoDiagnosticBag _diagnostics;

    private readonly CancellationToken _cancellationToken;

    // Index 0 holds the most recently used chunk; eviction removes from the end.
    private readonly List<CachedEntry> _entries = [];

    public StagedTerrainChunkCache(
        string stageDirectory,
        int chunksX,
        int chunksY,
        PayloadFileReference[] terrainReferences,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(stageDirectory);
        ArgumentNullException.ThrowIfNull(terrainReferences);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (!Path.IsPathRooted(stageDirectory))
        {
            throw new ArgumentException("The stage directory must be an absolute path.", nameof(stageDirectory));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(chunksX, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunksY, 1);

        if (terrainReferences.Length != (long)chunksX * chunksY)
        {
            throw new ArgumentException(
                $"The terrain reference array must hold one entry per chunk ({(long)chunksX * chunksY}); found {terrainReferences.Length}.",
                nameof(terrainReferences));
        }

        _stageDirectory = stageDirectory;
        _chunksX = chunksX;
        _chunksY = chunksY;
        _terrainReferences = terrainReferences;
        _diagnostics = diagnostics;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Verifies staged bytes before returning a cached or newly decoded terrain chunk.</summary>
    public TerrainChunk? Resolve(int chunkX, int chunkY)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkX);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkX, _chunksX);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkY);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkY, _chunksY);
        _cancellationToken.ThrowIfCancellationRequested();

        var reference = _terrainReferences[(chunkY * _chunksX) + chunkX];
        var relativePath = PayloadChunkPaths.Terrain(chunkX, chunkY);

        // Verification runs before any cache-hit return: a cached decode is only valid
        // for bytes proven identical to the write.
        if (!TryResolveCanonicalTarget(relativePath, out var fullPath))
        {
            return null;
        }

        if (!TryReadVerifiedBytes(relativePath, fullPath, reference, out var encoded))
        {
            return null;
        }

        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];

            if (entry.ChunkX == chunkX && entry.ChunkY == chunkY)
            {
                _entries.RemoveAt(i);
                _entries.Insert(0, entry);
                return entry.Chunk;
            }
        }

        var chunk = PayloadChunkCodec.DecodeTerrain(chunkX, chunkY, encoded, _diagnostics, _cancellationToken);

        if (chunk is null)
        {
            return null;
        }

        _entries.Insert(0, new CachedEntry(chunkX, chunkY, chunk));

        if (_entries.Count > Capacity)
        {
            _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        }

        return chunk;
    }

    /// <summary>Reads and verifies one fixed-size terrain file without trusting its declared length.</summary>
    private bool TryReadVerifiedBytes(string relativePath, string fullPath, PayloadFileReference reference, out byte[] encoded)
    {
        encoded = new byte[PayloadChunkDescriptor.TerrainChunkBytes];

        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var read = 0;

            while (read < encoded.Length)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var block = stream.Read(encoded, read, encoded.Length - read);

                if (block == 0)
                {
                    Fail(
                        ImportChunkGenerationDiagnostics.StagedTerrainCacheMutatedCode,
                        $"The staged terrain file '{relativePath}' holds only {read} bytes; {reference.LengthBytes} were written.",
                        fullPath);
                    return false;
                }

                read += block;
            }

            Span<byte> probe = stackalloc byte[1];

            if (stream.Read(probe) != 0)
            {
                Fail(
                    ImportChunkGenerationDiagnostics.StagedTerrainCacheMutatedCode,
                    $"The staged terrain file '{relativePath}' holds more than the {reference.LengthBytes} bytes written.",
                    fullPath);
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Fail(
                ImportChunkGenerationDiagnostics.StagedTerrainCacheInvalidCode,
                $"Reading the staged terrain file '{relativePath}' failed: {ex.Message}",
                fullPath,
                ex);
            return false;
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant();

        if (!string.Equals(actualHash, reference.Sha256Hex, StringComparison.Ordinal))
        {
            Fail(
                ImportChunkGenerationDiagnostics.StagedTerrainCacheMutatedCode,
                $"The staged terrain file '{relativePath}' hashes to {actualHash}; it no longer holds the written bytes {reference.Sha256Hex}.",
                fullPath);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Verifies the stage root and terrain subdirectory with no-follow semantics and
    /// returns the absolute file path; the file entry must exist and must not be a link.
    /// </summary>
    private bool TryResolveCanonicalTarget(string relativePath, out string fullPath)
    {
        fullPath = _stageDirectory;

        foreach (var segment in relativePath.Split('/'))
        {
            fullPath = Path.Combine(fullPath, segment);
        }

        try
        {
            if (TryResolveLinkTarget(_stageDirectory) is not null)
            {
                Fail(ImportChunkGenerationDiagnostics.StagedTerrainCacheInvalidCode, "The stage directory is a symbolic link; links are never followed.", _stageDirectory);
                return false;
            }

            if (!Directory.Exists(_stageDirectory))
            {
                Fail(ImportChunkGenerationDiagnostics.StagedTerrainCacheInvalidCode, $"The stage directory does not exist: '{_stageDirectory}'.", _stageDirectory);
                return false;
            }

            var directory = Path.GetDirectoryName(fullPath)!;

            if (TryResolveLinkTarget(directory) is not null)
            {
                Fail(ImportChunkGenerationDiagnostics.StagedTerrainCacheInvalidCode, $"The staged terrain directory is a symbolic link; links are never followed: '{directory}'.", directory);
                return false;
            }

            if (!Directory.Exists(directory))
            {
                Fail(ImportChunkGenerationDiagnostics.StagedTerrainCacheInvalidCode, $"The staged terrain directory does not exist: '{directory}'.", directory);
                return false;
            }

            if (TryResolveLinkTarget(fullPath) is not null)
            {
                Fail(ImportChunkGenerationDiagnostics.StagedTerrainCacheInvalidCode, $"The staged terrain file '{relativePath}' is a symbolic link; links are never followed.", fullPath);
                return false;
            }

            if (!File.Exists(fullPath))
            {
                Fail(ImportChunkGenerationDiagnostics.StagedTerrainCacheInvalidCode, $"The staged terrain file '{relativePath}' does not exist.", fullPath);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Fail(
                ImportChunkGenerationDiagnostics.StagedTerrainCacheInvalidCode,
                $"Inspecting the staged terrain target '{relativePath}' failed: {ex.Message}",
                fullPath,
                ex);
            return false;
        }
    }

    private void Fail(string code, string message, string? sourcePath = null, Exception? detail = null)
    {
        ImportChunkGenerationDiagnostics.AddError(_diagnostics, code, message, sourcePath, detail);
    }

    // Returns null only for an ordinary missing entry or a plain non-link; link resolution failures propagate.
    private static string? TryResolveLinkTarget(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: false)?.FullName;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private readonly record struct CachedEntry(int ChunkX, int ChunkY, TerrainChunk Chunk);
}
