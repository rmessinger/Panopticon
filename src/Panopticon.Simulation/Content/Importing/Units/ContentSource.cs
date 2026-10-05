#nullable enable

using System;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// A unit-content source game. Name equality follows the source's own rule, because Generals INI
/// names are case-insensitive while EE2 DDF names are matched ordinally.
/// </summary>
public sealed class ContentSource : IEquatable<ContentSource>
{
    public static readonly ContentSource Generals = new("generals", caseSensitiveNames: false);

    public static readonly ContentSource Ee2 = new("ee2", caseSensitiveNames: true);

    public string Id { get; }

    public bool CaseSensitiveNames { get; }

    public StringComparer NameComparer => CaseSensitiveNames ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public ContentSource(string id, bool caseSensitiveNames)
    {
        Ensure.NotNullOrWhiteSpace(id, "Content source id must not be empty.");

        Id = id;
        CaseSensitiveNames = caseSensitiveNames;
    }

    public bool Equals(ContentSource? other) => other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ContentSource);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);

    public override string ToString() => Id;
}
