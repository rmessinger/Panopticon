#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Inventory entry for one companion member text file in the package manifest.</summary>
public sealed record MapPackageCompanionEntry
{
    [JsonConstructor]
    public MapPackageCompanionEntry(string name, string path, long byteLength, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        Name = name;
        Path = string.IsNullOrWhiteSpace(path)
            ? (name.StartsWith("companions/", StringComparison.OrdinalIgnoreCase) ? name : $"companions/{name}")
            : path;
        ByteLength = byteLength;
        Sha256 = sha256;
    }

    public MapPackageCompanionEntry(string name, string sha256)
        : this(name, name.StartsWith("companions/", StringComparison.OrdinalIgnoreCase) ? name : $"companions/{name}", 0, sha256)
    {
    }

    public string Name { get; init; }

    public string Path { get; init; }

    public long ByteLength { get; init; }

    public string Sha256 { get; init; }
}
