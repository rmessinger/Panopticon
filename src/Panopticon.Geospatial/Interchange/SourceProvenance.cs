using System;
using System.Linq;

namespace Panopticon.Geospatial.Interchange;

/// <summary>Provenance of one imported source file. <see cref="Sha256Hex"/> is stored lowercase.</summary>
public sealed record SourceProvenance
{
    private const int Sha256HexLength = 64;

    public SourceProvenance(string sourceId, string relativePath, string sha256Hex, long lengthBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        ArgumentException.ThrowIfNullOrEmpty(sha256Hex);
        ArgumentOutOfRangeException.ThrowIfNegative(lengthBytes);

        var normalized = sha256Hex.ToLowerInvariant();
        if (normalized.Length != Sha256HexLength || !normalized.All(IsLowerCaseHexDigit))
        {
            throw new ArgumentException("SHA-256 must be 64 hexadecimal characters.", nameof(sha256Hex));
        }

        SourceId = sourceId;
        RelativePath = relativePath;
        Sha256Hex = normalized;
        LengthBytes = lengthBytes;
    }

    /// <summary>Stable identifier of the source within the import.</summary>
    public string SourceId { get; }

    /// <summary>Path of the source relative to the import root.</summary>
    public string RelativePath { get; }

    /// <summary>SHA-256 hash of the source bytes as 64 lowercase hex characters.</summary>
    public string Sha256Hex { get; }

    public long LengthBytes { get; }

    private static bool IsLowerCaseHexDigit(char character)
    {
        return character is >= '0' and <= '9' or >= 'a' and <= 'f';
    }
}
