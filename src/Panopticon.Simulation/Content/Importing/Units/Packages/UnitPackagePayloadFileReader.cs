#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// Verified reader for payload and retained binary files under a unit package root.
/// Verifies path containment, absence of symlinks, byte length, and SHA-256 before decoding.
/// </summary>
public static class UnitPackagePayloadFileReader
{
    public static bool TryReadVerifiedBytes(
        string packageRoot,
        string entryPath,
        long expectedByteLength,
        string expectedSha256,
        [NotNullWhen(true)] out byte[]? bytes,
        [NotNullWhen(false)] out string? errorCode,
        [NotNullWhen(false)] out string? errorMessage)
    {
        bytes = null;
        errorCode = null;
        errorMessage = null;

        if (expectedByteLength > UnitPackageFormat.MaxPayloadFileBytes)
        {
            errorCode = UnitPackageDiagnosticCodes.ResourceLimitExceeded;
            errorMessage = $"Package file '{entryPath}' declared byte length {expectedByteLength} exceeds limit of {UnitPackageFormat.MaxPayloadFileBytes} bytes.";
            return false;
        }

        if (!UnitPackageFileGuard.TryResolve(packageRoot, entryPath, out var fullPath, out errorCode, out errorMessage))
        {
            return false;
        }

        var fileInfo = new FileInfo(fullPath);

        if (!fileInfo.Exists)
        {
            errorCode = UnitPackageDiagnosticCodes.FileMissing;
            errorMessage = $"Package file '{entryPath}' does not exist under '{packageRoot}'.";
            return false;
        }

        if (fileInfo.Length != expectedByteLength)
        {
            errorCode = UnitPackageDiagnosticCodes.LengthMismatch;
            errorMessage = $"Package file '{entryPath}' holds {fileInfo.Length} bytes; manifest entry declares {expectedByteLength}.";
            return false;
        }

        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            errorCode = UnitPackageDiagnosticCodes.IoFailure;
            errorMessage = $"Reading package file '{entryPath}' failed: {ex.Message}";
            return false;
        }

        if (!UnitPackagePath.IsValidSha256(expectedSha256))
        {
            errorCode = UnitPackageDiagnosticCodes.DigestInvalid;
            errorMessage = $"Manifest digest '{expectedSha256}' for '{entryPath}' is not 64 lowercase hex characters.";
            bytes = null;
            return false;
        }

        var actualSha256 = ComputeSha256Hex(bytes);

        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
        {
            errorCode = UnitPackageDiagnosticCodes.DigestMismatch;
            errorMessage = $"Package file '{entryPath}' hashes to {actualSha256}; manifest entry declares {expectedSha256}.";
            bytes = null;
            return false;
        }

        return true;
    }

    public static string ComputeSha256Hex(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
