using System;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>
/// Reference to one payload content file: a canonical payload-relative path, the exact
/// byte length, and the lowercase SHA-256 of those bytes.
/// </summary>
public sealed record PayloadFileReference
{
    public PayloadFileReference(string path, long lengthBytes, string sha256Hex)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        PayloadValidationRules.NormalizedRelativePath(path, nameof(path));
        ArgumentOutOfRangeException.ThrowIfNegative(lengthBytes);
        PayloadValidationRules.Sha256Hex(sha256Hex, nameof(sha256Hex));

        Path = path;
        LengthBytes = lengthBytes;
        Sha256Hex = sha256Hex;
    }

    /// <summary>Payload-relative path with '/' separators, for example <c>terrain/0_0.bin</c>.</summary>
    public string Path { get; }

    public long LengthBytes { get; }

    /// <summary>SHA-256 of the referenced bytes as 64 lowercase hex characters.</summary>
    public string Sha256Hex { get; }
}
