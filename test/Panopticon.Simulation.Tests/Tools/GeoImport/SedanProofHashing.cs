using System;
using System.IO;
using System.Security.Cryptography;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>Constant-memory SHA-256 for proof-root hashing; files may exceed the int-length ReadAllBytes limit.</summary>
public static class SedanProofHashing
{
    /// <summary>Hashes the bytes readable from the stream to end of stream; never uses Length.</summary>
    public static string Sha256Hex(Stream stream)
    {
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Hashes a file read-only, disposing the handle when done.</summary>
    public static string Sha256FileHex(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var stream = File.OpenRead(path);
        return Sha256Hex(stream);
    }
}
