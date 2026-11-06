#nullable enable

using System;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Retained-opaque legacy section. Bytes round-trip byte-exact and are accepted only for a known section name and version. Bytes are copied at construction.</summary>
public sealed record RetainedSection
{
    public RetainedSection(string sectionName, int sectionVersion, ReadOnlyMemory<byte> bytes, string sha256)
    {
        ArgumentNullException.ThrowIfNull(sectionName);
        ArgumentNullException.ThrowIfNull(sha256);
        SectionName = sectionName;
        SectionVersion = sectionVersion;
        Bytes = bytes.ToArray();
        Sha256 = sha256;
    }

    public string SectionName { get; }

    public int SectionVersion { get; }

    /// <summary>Defensive copy; mutating the source array after construction does not change the section bytes.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    public string Sha256 { get; }
}
