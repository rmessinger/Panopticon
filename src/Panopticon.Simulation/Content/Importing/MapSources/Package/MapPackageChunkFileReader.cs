#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>
/// Digest-verified reader for one package chunk file. The path is resolved through
/// <see cref="MapPackageFileGuard"/>, byte length and lowercase-hex SHA-256 are verified
/// against the manifest entry before anything decodes, and every rejection is a typed
/// diagnostic that returns no data.
/// </summary>
public static class MapPackageChunkFileReader
{
    public static MapPackageChunkReadResult<float[]> ReadHeights(string packageRoot, MapPackageLayerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!RequireKindAndEncoding(entry, MapPackageFormat.LayerKindHeights, MapPackageFormat.EncodingFloat32Le, out var kindError))
        {
            return Fail<float[]>(kindError);
        }

        if (!TryReadVerifiedBytes(packageRoot, entry.Path, entry.ByteLength, entry.Sha256, out var bytes, out var readError))
        {
            return Fail<float[]>(readError);
        }

        if (!MapPackageChunkCodec.TryDecodeHeights(bytes, out var samples, out var decodeError))
        {
            return Fail<float[]>(decodeError);
        }

        return new(samples, []);
    }

    public static MapPackageChunkReadResult<byte[]> ReadMaterials(string packageRoot, MapPackageLayerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!RequireKindAndEncoding(entry, MapPackageFormat.LayerKindMaterials, MapPackageFormat.EncodingUint8, out var kindError))
        {
            return Fail<byte[]>(kindError);
        }

        if (!TryReadVerifiedBytes(packageRoot, entry.Path, entry.ByteLength, entry.Sha256, out var bytes, out var readError))
        {
            return Fail<byte[]>(readError);
        }

        return new(MapPackageChunkCodec.DecodeMaterials(bytes), []);
    }

    public static MapPackageChunkReadResult<byte[]> ReadMasks(string packageRoot, MapPackageLayerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!RequireKindAndEncoding(entry, MapPackageFormat.LayerKindMasks, MapPackageFormat.EncodingUint8, out var kindError))
        {
            return Fail<byte[]>(kindError);
        }

        if (!TryReadVerifiedBytes(packageRoot, entry.Path, entry.ByteLength, entry.Sha256, out var bytes, out var readError))
        {
            return Fail<byte[]>(readError);
        }

        return new(MapPackageChunkCodec.DecodeMasks(bytes), []);
    }

    public static MapPackageChunkReadResult<byte[]> ReadImagery(string packageRoot, MapPackageLayerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!RequireKindAndEncoding(entry, MapPackageFormat.LayerKindImagery, MapPackageFormat.EncodingRgba8, out var kindError))
        {
            return Fail<byte[]>(kindError);
        }

        if (!TryReadVerifiedBytes(packageRoot, entry.Path, entry.ByteLength, entry.Sha256, out var bytes, out var readError))
        {
            return Fail<byte[]>(readError);
        }

        return new(MapPackageChunkCodec.DecodeImagery(bytes), []);
    }

    public static MapPackageChunkReadResult<byte[]> ReadRetained(string packageRoot, MapPackageRetainedEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!TryReadVerifiedBytes(packageRoot, entry.Path, entry.ByteLength, entry.Sha256, out var bytes, out var readError))
        {
            return Fail<byte[]>(readError);
        }

        return new(MapPackageChunkCodec.DecodeRetained(bytes), []);
    }

    private static bool RequireKindAndEncoding(
        MapPackageLayerEntry entry,
        string expectedKind,
        string expectedEncoding,
        [NotNullWhen(false)] out MapPackageDiagnostic? error)
    {
        if (string.Equals(entry.LayerKind, expectedKind, StringComparison.Ordinal)
            && string.Equals(entry.Encoding, expectedEncoding, StringComparison.Ordinal))
        {
            error = null;
            return true;
        }

        error = new MapPackageDiagnostic(
            MapPackageChunkDiagnostic.EncodingMismatch,
            $"Layer entry ({entry.ChunkX}, {entry.ChunkY}) declares kind '{entry.LayerKind}' and encoding '{entry.Encoding}'; this read requires kind '{expectedKind}' with encoding '{expectedEncoding}'.");
        return false;
    }

    private static bool TryReadVerifiedBytes(
        string packageRoot,
        string entryPath,
        long expectedByteLength,
        string expectedSha256,
        [NotNullWhen(true)] out byte[]? bytes,
        [NotNullWhen(false)] out MapPackageDiagnostic? error)
    {
        bytes = null;
        error = null;

        if (expectedByteLength > MapPackageFormat.MaxPackageFileBytes)
        {
            error = new MapPackageDiagnostic(
                MapPackageDiagnostic.ResourceLimitExceeded,
                $"Package file '{entryPath}' declared byte length {expectedByteLength} exceeds the maximum allowed {MapPackageFormat.MaxPackageFileBytes} bytes.");
            return false;
        }

        if (!MapPackageFileGuard.TryResolve(packageRoot, entryPath, out var fullPath, out var guardError))
        {
            error = guardError;
            return false;
        }

        var fileInfo = new FileInfo(fullPath);

        if (!fileInfo.Exists)
        {
            error = new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.FileMissing,
                $"Package file '{entryPath}' does not exist under '{packageRoot}'.");
            return false;
        }

        if (fileInfo.Length != expectedByteLength)
        {
            error = new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.LengthMismatch,
                $"Package file '{entryPath}' holds {fileInfo.Length} bytes; the manifest entry declares {expectedByteLength}.");
            return false;
        }

        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.IoFailure,
                $"Reading package file '{entryPath}' failed: {ex.Message}");
            return false;
        }

        if (!MapPackagePath.IsValidSha256(expectedSha256))
        {
            error = new MapPackageDiagnostic(
                MapPackageDiagnostic.DigestInvalid,
                $"Manifest digest '{expectedSha256}' for '{entryPath}' is not 64 lowercase hex characters.");
            bytes = null;
            return false;
        }

        var actualSha256 = ComputeSha256Hex(bytes);

        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
        {
            error = new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.DigestMismatch,
                $"Package file '{entryPath}' hashes to {actualSha256}; the manifest entry declares {expectedSha256}.");
            bytes = null;
            return false;
        }

        return true;
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static MapPackageChunkReadResult<T> Fail<T>(MapPackageDiagnostic error)
        where T : class
    {
        return new(null, [error]);
    }
}
