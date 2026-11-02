using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial;

namespace Panopticon.Geospatial.Payload.Reading;

/// <summary>
/// Bounded, fail-closed file access for one opened payload root: link, length, and
/// lowercase SHA-256 checks on every read, cache hits included. I/O and permission
/// failures become typed diagnostics; cancellation and programmer errors propagate.
/// </summary>
internal sealed class PayloadChunkFileStore
{
    private const int CopyBufferBytes = 64 * 1024;

    private readonly string _root;

    private readonly GeospatialImportLimits _limits;

    internal PayloadChunkFileStore(string root, GeospatialImportLimits limits)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentNullException.ThrowIfNull(limits);

        _root = root;
        _limits = limits;
    }

    /// <summary>Opens one payload-relative file for reading, rejecting links first; null with a diagnostic on failure.</summary>
    internal FileStream? TryOpenRead(string relativePath, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!PayloadRootInventory.TryValidateKnownPathChain(_root, relativePath, diagnostics))
        {
            return null;
        }

        var path = AbsolutePath(relativePath);

        try
        {
            var attributes = File.GetAttributes(path);

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                PayloadReaderDiagnostics.Error(
                    diagnostics,
                    PayloadReaderDiagnosticCodes.InventoryLink,
                    $"The payload file '{relativePath}' is a symbolic link; payload files must be regular files.");

                return null;
            }

            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.FileUnreadable,
                $"The payload file '{relativePath}' could not be opened: {ex.Message}",
                ex);

            return null;
        }
    }

    /// <summary>
    /// Verifies the length and hash of one file by streaming it, without buffering the
    /// content; used to revalidate cached chunks on lazy reads.
    /// </summary>
    internal bool TryVerify(
        string relativePath,
        long expectedLength,
        string expectedSha256Hex,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        using var stream = TryOpenRead(relativePath, diagnostics, cancellationToken);

        if (stream is null)
        {
            return false;
        }

        try
        {
            if (stream.Length != expectedLength)
            {
                LengthMismatch(diagnostics, relativePath, stream.Length, expectedLength);

                return false;
            }

            var actual = HashExactly(stream, expectedLength, relativePath, diagnostics, cancellationToken);

            return actual is not null && HashMatches(actual, expectedSha256Hex, relativePath, diagnostics);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ReadFailure(diagnostics, relativePath, ex);

            return false;
        }
    }

    /// <summary>
    /// Reads one file fully after the link, length, and hash checks; the returned buffer
    /// is the exact descriptor length. Never allocates before the length is known.
    /// </summary>
    internal byte[]? TryReadVerified(
        string relativePath,
        long expectedLength,
        string expectedSha256Hex,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        using var stream = TryOpenRead(relativePath, diagnostics, cancellationToken);

        if (stream is null)
        {
            return null;
        }

        try
        {
            if (stream.Length != expectedLength)
            {
                LengthMismatch(diagnostics, relativePath, stream.Length, expectedLength);

                return null;
            }

            if (expectedLength > int.MaxValue)
            {
                PayloadReaderDiagnostics.Error(
                    diagnostics,
                    PayloadReaderDiagnosticCodes.ImageryBudgetExceeded,
                    $"The payload file '{relativePath}' declares {expectedLength} bytes, beyond the largest allocatable array.");

                return null;
            }

            var content = new byte[expectedLength];
            var total = 0;

            while (total < content.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var read = stream.Read(content, total, content.Length - total);

                if (read == 0)
                {
                    PayloadReaderDiagnostics.Error(
                        diagnostics,
                        PayloadReaderDiagnosticCodes.FileMutated,
                        $"The payload file '{relativePath}' ended after {total} of the expected {expectedLength} bytes.");

                    return null;
                }

                total += read;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

            return HashMatches(actual, expectedSha256Hex, relativePath, diagnostics) ? content : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ReadFailure(diagnostics, relativePath, ex);

            return null;
        }
    }

    /// <summary>
    /// Hashes one JSON root file while enforcing its MaxPayloadMetadataBytes bound; a
    /// file that grew past the bound after parsing counts as mutation.
    /// </summary>
    internal string? TryHashBoundedJson(
        string fileName,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        using var stream = TryOpenRead(fileName, diagnostics, cancellationToken);

        if (stream is null)
        {
            return null;
        }

        try
        {
            var maxBytes = _limits.MaxPayloadMetadataBytes;
            var buffer = new byte[CopyBufferBytes];
            using var hash = SHA256.Create();
            long total = 0;
            int read;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                read = stream.Read(buffer, 0, buffer.Length);
                total += read;

                if (total > maxBytes)
                {
                    PayloadReaderDiagnostics.Error(
                        diagnostics,
                        PayloadReaderDiagnosticCodes.FileMutated,
                        $"The file '{fileName}' changed during open: it now exceeds the maxPayloadMetadataBytes bound of {maxBytes}.");

                    return null;
                }

                if (read > 0)
                {
                    hash.TransformBlock(buffer, 0, read, null, 0);
                }
            }
            while (read > 0);

            hash.TransformFinalBlock([], 0, 0);

            return Convert.ToHexString(hash.Hash!).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ReadFailure(diagnostics, fileName, ex);

            return null;
        }
    }

    private string? HashExactly(
        FileStream stream,
        long expectedLength,
        string relativePath,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[CopyBufferBytes];
        using var hash = SHA256.Create();
        long total = 0;

        while (total < expectedLength)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var read = stream.ReadAtLeast(buffer, (int)Math.Min(buffer.Length, expectedLength - total));

            if (read == 0)
            {
                PayloadReaderDiagnostics.Error(
                    diagnostics,
                    PayloadReaderDiagnosticCodes.FileMutated,
                    $"The payload file '{relativePath}' ended after {total} of the expected {expectedLength} bytes.");

                return null;
            }

            hash.TransformBlock(buffer, 0, read, null, 0);
            total += read;
        }

        hash.TransformFinalBlock([], 0, 0);

        return Convert.ToHexString(hash.Hash!).ToLowerInvariant();
    }

    private static bool HashMatches(string actual, string expected, string relativePath, GeoDiagnosticBag diagnostics)
    {
        if (string.Equals(actual, expected, StringComparison.Ordinal))
        {
            return true;
        }

        PayloadReaderDiagnostics.Error(
            diagnostics,
            PayloadReaderDiagnosticCodes.FileMutated,
            $"The payload file '{relativePath}' hashes to {actual} but the descriptor declares {expected}.");

        return false;
    }

    private static void LengthMismatch(GeoDiagnosticBag diagnostics, string relativePath, long actual, long expected)
    {
        PayloadReaderDiagnostics.Error(
            diagnostics,
            PayloadReaderDiagnosticCodes.FileMutated,
            $"The payload file '{relativePath}' holds {actual} bytes but the descriptor declares {expected}.");
    }

    private static void ReadFailure(GeoDiagnosticBag diagnostics, string relativePath, Exception ex)
    {
        PayloadReaderDiagnostics.Error(
            diagnostics,
            PayloadReaderDiagnosticCodes.FileUnreadable,
            $"The payload file '{relativePath}' could not be read: {ex.Message}",
            ex);
    }

    private string AbsolutePath(string relativePath)
    {
        return Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
