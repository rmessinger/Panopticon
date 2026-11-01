using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Serialization;

namespace Panopticon.Geospatial.Payload.Storage;

/// <summary>
/// Writes one transaction's staged payload files under the staged-write contract of
/// docs/roadmap/implementation/track-r-r2-payload.md: one-time create-new files,
/// no-follow checks on the stage root, ancestors, and targets, streamed bounded JSON
/// with actual-byte hashes, report rewrite via sibling temp plus atomic rename, and
/// owned-only cleanup. Not thread-safe.
/// </summary>
internal sealed class PayloadStagedFiles
{
    private const int PngSignatureBytes = 8;

    private const int PngChunkHeaderBytes = 8;

    private const int PngIhdrDataBytes = 13;

    private readonly string _stageDirectory;
    private readonly GeospatialImportLimits _limits;
    private readonly GeoDiagnosticBag _diagnostics;
    private string? _metadataSha256Hex;
    private bool _reportWritten;

    internal PayloadStagedFiles(string stageDirectory, GeospatialImportLimits limits, GeoDiagnosticBag diagnostics)
    {
        ArgumentException.ThrowIfNullOrEmpty(stageDirectory);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (!Path.IsPathRooted(stageDirectory))
        {
            throw new ArgumentException("The stage directory must be an absolute path.", nameof(stageDirectory));
        }

        _stageDirectory = stageDirectory;
        _limits = limits;
        _diagnostics = diagnostics;
    }

    /// <summary>Encodes one terrain chunk and writes its one-time file; null with a diagnostic on recoverable failure.</summary>
    internal PayloadFileReference? WriteTerrain(TerrainChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var encoded = PayloadChunkCodec.EncodeTerrain(chunk, cancellationToken);

        return WriteOneTimeBytes(PayloadChunkPaths.Terrain(chunk.ChunkX, chunk.ChunkY), encoded, cancellationToken);
    }

    /// <summary>Encodes one material chunk and writes its one-time file; the palette itself travels in payload.json.</summary>
    internal PayloadFileReference? WriteMaterial(MaterialChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var encoded = PayloadChunkCodec.EncodeMaterial(chunk, cancellationToken);

        return WriteOneTimeBytes(PayloadChunkPaths.Materials(chunk.ChunkX, chunk.ChunkY), encoded, cancellationToken);
    }

    /// <summary>Encodes one terrain mask chunk and writes its one-time file; null with a diagnostic on recoverable failure.</summary>
    internal PayloadFileReference? WriteMask(TerrainMaskChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var encoded = PayloadChunkCodec.EncodeMask(chunk, cancellationToken);

        return WriteOneTimeBytes(PayloadChunkPaths.Masks(chunk.ChunkX, chunk.ChunkY), encoded, cancellationToken);
    }

    /// <summary>
    /// Writes one imagery chunk's already-encoded PNG bytes without re-encoding; only the
    /// fixed IHDR and the raster limits are checked, the reader fully validates the PNG.
    /// </summary>
    internal PayloadFileReference? WriteImagery(ImageryChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var relativePath = PayloadChunkPaths.Imagery(chunk.ChunkX, chunk.ChunkY);

        if (!TryValidateImageryHeader(chunk, relativePath))
        {
            return null;
        }

        return WriteOneTimeBytes(relativePath, chunk.Png, cancellationToken);
    }

    /// <summary>
    /// Streams payload.json through the accepted bounded JSON writer and returns the
    /// SHA-256 of the actually written bytes; one-time, like every staged file.
    /// </summary>
    internal string? WriteMetadata(GeneratedMapPayloadMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var reference = WriteOneTimeStream(
            PayloadChunkPaths.MetadataFileName,
            StagedFileDiagnostics.MetadataWriteFailed,
            (stream, token) => PayloadJson.WriteMetadata(stream, metadata, _limits, token),
            cancellationToken);

        if (reference is null)
        {
            return null;
        }

        _metadataSha256Hex = reference.Sha256Hex;
        return _metadataSha256Hex;
    }

    /// <summary>
    /// Writes report.json bound to the actually written payload.json hash. The first
    /// write is one-time; later calls rewrite the owned report per the staged-write
    /// contract: unique sibling temp, then an atomic replace that never follows a link.
    /// </summary>
    internal bool WriteReport(GeneratedMapPayloadReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (_metadataSha256Hex is null)
        {
            Fail(
                StagedFileDiagnostics.ReportBeforeMetadata,
                "report.json cannot be written before payload.json; there is no metadata hash to bind the report to.",
                PayloadChunkPaths.ReportFileName);
            return false;
        }

        if (!string.Equals(report.PayloadSha256Hex, _metadataSha256Hex, StringComparison.Ordinal))
        {
            Fail(
                StagedFileDiagnostics.ReportHashMismatch,
                $"The report declares the payload hash {report.PayloadSha256Hex} but the written payload.json hashes to {_metadataSha256Hex}.",
                PayloadChunkPaths.ReportFileName);
            return false;
        }

        if (_reportWritten)
        {
            return RewriteReport(report, cancellationToken);
        }

        var reference = WriteOneTimeStream(
            PayloadChunkPaths.ReportFileName,
            StagedFileDiagnostics.ReportWriteFailed,
            (stream, token) => PayloadJson.WriteReport(stream, report, _limits, token),
            cancellationToken);

        if (reference is null)
        {
            return false;
        }

        _reportWritten = true;
        return true;
    }

    private PayloadFileReference? WriteOneTimeBytes(string relativePath, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        return WriteOneTimeStream(
            relativePath,
            StagedFileDiagnostics.StagedWriteFailed,
            (stream, _) => stream.Write(payload.Span),
            cancellationToken);
    }

    private PayloadFileReference? WriteOneTimeStream(
        string relativePath,
        string resourceFailureCode,
        Action<Stream, CancellationToken> writeContent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTarget(relativePath, allowExistingTarget: false, out var fullPath))
        {
            return null;
        }

        if (!TryWriteNewFile(fullPath, relativePath, resourceFailureCode, writeContent, cancellationToken, out var lengthBytes, out var sha256Hex))
        {
            return null;
        }

        return new PayloadFileReference(relativePath, lengthBytes, sha256Hex);
    }

    private bool RewriteReport(GeneratedMapPayloadReport report, CancellationToken cancellationToken)
    {
        if (!TryResolveTarget(PayloadChunkPaths.ReportFileName, allowExistingTarget: true, out var reportPath))
        {
            return false;
        }

        // A unique owned sibling temp, then rename over the report entry: rename replaces
        // the destination entry itself, so even a report entry swapped for a link between
        // the check and the rename is replaced as an entry and its target is never
        // written through.
        var tempPath = reportPath + ".tmp-" + Guid.NewGuid().ToString("N");

        if (!TryWriteNewFile(
                tempPath,
                Path.GetFileName(tempPath),
                StagedFileDiagnostics.ReportWriteFailed,
                (stream, token) => PayloadJson.WriteReport(stream, report, _limits, token),
                cancellationToken,
                out _,
                out _))
        {
            return false;
        }

        try
        {
            File.Move(tempPath, reportPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DeleteOwnedPartial(tempPath, Path.GetFileName(tempPath));
            Fail(
                StagedFileDiagnostics.ReportWriteFailed,
                $"Atomically replacing the staged file '{PayloadChunkPaths.ReportFileName}' failed: {ex.Message}",
                reportPath,
                ex);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Creates one exclusive new file and streams content through the hashing wrapper.
    /// The owned partial file is removed on failure or cancellation; an entry that
    /// already exists fails the create itself and is never deleted.
    /// </summary>
    private bool TryWriteNewFile(
        string fullPath,
        string relativePath,
        string resourceFailureCode,
        Action<Stream, CancellationToken> writeContent,
        CancellationToken cancellationToken,
        out long lengthBytes,
        out string sha256Hex)
    {
        lengthBytes = 0;
        sha256Hex = string.Empty;

        FileStream file;

        try
        {
            file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // CreateNew never writes through an existing entry, links included; a failure
            // here means the entry exists or the resource refused, and nothing is deleted.
            Fail(resourceFailureCode, $"Creating the staged file '{relativePath}' failed: {ex.Message}", fullPath, ex);
            return false;
        }

        PayloadHashingWriteStream hashing;

        try
        {
            hashing = new PayloadHashingWriteStream(file, cancellationToken);
        }
        catch
        {
            // The wrapper failed to construct after the file opened, so the handle is
            // disposed here; nothing was ever written through it.
            file.Dispose();
            throw;
        }

        try
        {
            using (file)
            {
                using (hashing)
                {
                    writeContent(hashing, cancellationToken);

                    // The finalized hash and the length are read before either owned
                    // object is disposed: the hash state dies with the wrapper's Dispose.
                    lengthBytes = hashing.WrittenBytes;
                    sha256Hex = hashing.Sha256Hex();
                }
            }
        }
        catch (OperationCanceledException)
        {
            DeleteOwnedPartial(fullPath, relativePath);
            throw;
        }
        catch (PayloadJsonLimitException ex)
        {
            DeleteOwnedPartial(fullPath, relativePath);
            Fail(
                StagedFileDiagnostics.MetadataLimitExceeded,
                $"The staged file '{relativePath}' exceeded the maxPayloadMetadataBytes bound of {_limits.MaxPayloadMetadataBytes} bytes: {ex.Message}",
                fullPath,
                ex);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DeleteOwnedPartial(fullPath, relativePath);
            Fail(resourceFailureCode, $"Writing the staged file '{relativePath}' failed: {ex.Message}", fullPath, ex);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates the staging root and every existing ancestor of the relative path with
    /// no-follow semantics and returns the absolute target path; the target entry itself
    /// must never be a link, and one-time targets must not exist.
    /// </summary>
    private bool TryResolveTarget(string relativePath, bool allowExistingTarget, out string fullPath)
    {
        fullPath = string.Empty;

        if (!TryVerifyStageRoot())
        {
            return false;
        }

        var segments = relativePath.Split('/');
        var current = _stageDirectory;

        for (var index = 0; index < segments.Length - 1; index++)
        {
            current = Path.Combine(current, segments[index]);

            if (!TryEnsureSubdirectory(current, segments[index]))
            {
                return false;
            }
        }

        fullPath = Path.Combine(current, segments[^1]);

        try
        {
            if (TryResolveLinkTarget(fullPath) is not null)
            {
                Fail(
                    StagedFileDiagnostics.StagedFileAlreadyExists,
                    $"The staged file '{relativePath}' exists as a symbolic link; links are never followed or replaced.",
                    fullPath);
                return false;
            }

            if (!allowExistingTarget && File.Exists(fullPath))
            {
                Fail(
                    StagedFileDiagnostics.StagedFileAlreadyExists,
                    $"The staged file '{relativePath}' already exists; one-time staged files are never overwritten.",
                    fullPath);
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Fail(
                StagedFileDiagnostics.StagedWriteFailed,
                $"Inspecting the staged file target '{relativePath}' failed: {ex.Message}",
                fullPath,
                ex);
            return false;
        }

        return true;
    }

    private bool TryVerifyStageRoot()
    {
        try
        {
            if (TryResolveLinkTarget(_stageDirectory) is not null)
            {
                Fail(
                    StagedFileDiagnostics.StageRootInvalid,
                    $"The staging directory is a symbolic link; links are never followed: '{_stageDirectory}'.",
                    _stageDirectory);
                return false;
            }

            if (File.Exists(_stageDirectory))
            {
                Fail(
                    StagedFileDiagnostics.StageRootInvalid,
                    $"The staging directory path exists as a file: '{_stageDirectory}'.",
                    _stageDirectory);
                return false;
            }

            if (!Directory.Exists(_stageDirectory))
            {
                Fail(
                    StagedFileDiagnostics.StageRootInvalid,
                    $"The staging directory does not exist: '{_stageDirectory}'.",
                    _stageDirectory);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Fail(
                StagedFileDiagnostics.StageRootInvalid,
                $"Inspecting the staging directory failed: {ex.Message}",
                _stageDirectory,
                ex);
            return false;
        }
    }

    private bool TryEnsureSubdirectory(string fullPath, string name)
    {
        try
        {
            if (TryResolveLinkTarget(fullPath) is not null)
            {
                Fail(
                    StagedFileDiagnostics.StageSubdirectoryInvalid,
                    $"The staged subdirectory '{name}' exists as a symbolic link; links are never followed.",
                    fullPath);
                return false;
            }

            if (File.Exists(fullPath))
            {
                Fail(
                    StagedFileDiagnostics.StageSubdirectoryInvalid,
                    $"The staged subdirectory '{name}' exists as a file.",
                    fullPath);
                return false;
            }

            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);

                // A link can appear between the check and the create; the created entry is
                // re-checked so a followed link is rejected before anything is written.
                if (TryResolveLinkTarget(fullPath) is not null)
                {
                    Fail(
                        StagedFileDiagnostics.StageSubdirectoryInvalid,
                        $"The staged subdirectory '{name}' was created through a symbolic link; it is left untouched.",
                        fullPath);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Fail(
                StagedFileDiagnostics.StageSubdirectoryInvalid,
                $"Preparing the staged subdirectory '{name}' failed: {ex.Message}",
                fullPath,
                ex);
            return false;
        }
    }

    /// <summary>
    /// Checks the fixed square 8-bit RGBA non-interlaced IHDR against the chunk's
    /// declared pixel size and the raster limits without decoding or promising pixel
    /// memory; the reader still fully validates the PNG.
    /// </summary>
    private bool TryValidateImageryHeader(ImageryChunk chunk, string relativePath)
    {
        var png = chunk.Png.Span;

        if (png.Length < PngSignatureBytes + PngChunkHeaderBytes + PngIhdrDataBytes
            || BinaryPrimitives.ReadUInt32BigEndian(png.Slice(PngSignatureBytes)) != PngIhdrDataBytes
            || !png.Slice(PngSignatureBytes + 4, 4).SequenceEqual("IHDR"u8))
        {
            Fail(
                StagedFileDiagnostics.ImageryHeaderInvalid,
                $"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): the imagery PNG does not open with a 13-byte IHDR chunk.",
                relativePath);
            return false;
        }

        var header = png.Slice(PngSignatureBytes + PngChunkHeaderBytes, PngIhdrDataBytes);
        var width = BinaryPrimitives.ReadInt32BigEndian(header);
        var height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(4));

        if (width != chunk.PixelsPerAxis || height != chunk.PixelsPerAxis)
        {
            Fail(
                StagedFileDiagnostics.ImageryHeaderInvalid,
                $"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): the imagery IHDR declares {width} x {height} pixels but the chunk declares {chunk.PixelsPerAxis} x {chunk.PixelsPerAxis}.",
                relativePath);
            return false;
        }

        if (header[8] != 8 || header[9] != 6 || header[10] != 0 || header[11] != 0 || header[12] != 0)
        {
            Fail(
                StagedFileDiagnostics.ImageryHeaderInvalid,
                $"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): the imagery IHDR is not the fixed 8-bit RGBA non-interlaced format (bit depth {header[8]}, color type {header[9]}, compression {header[10]}, filter {header[11]}, interlace {header[12]}).",
                relativePath);
            return false;
        }

        if (width > _limits.MaxRasterWidth || height > _limits.MaxRasterHeight)
        {
            Fail(
                StagedFileDiagnostics.ImageryBudgetExceeded,
                $"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): the imagery IHDR declares {width} x {height} pixels, beyond the {_limits.MaxRasterWidth} x {_limits.MaxRasterHeight} raster dimension limits.",
                relativePath);
            return false;
        }

        return true;
    }

    private void DeleteOwnedPartial(string fullPath, string relativePath)
    {
        try
        {
            // Only entries the current call created are named here, and the delete removes
            // a link as an entry, never its target.
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _diagnostics.Add(new GeoDiagnostic(
                StagedFileDiagnostics.StagedCleanupFailed,
                GeoStage.Write,
                GeoSeverity.Warning,
                $"Removing the failed partial staged file '{relativePath}' failed; the staging directory cleanup decides its fate.",
                fullPath,
                ex));
        }
    }

    private void Fail(string code, string message, string? sourcePath = null, Exception? detail = null)
    {
        _diagnostics.Add(new GeoDiagnostic(code, GeoStage.Write, GeoSeverity.Error, message, sourcePath, detail));
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
}
