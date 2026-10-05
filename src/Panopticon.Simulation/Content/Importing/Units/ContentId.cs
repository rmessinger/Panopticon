#nullable enable

using System;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Source-qualified typed identity. The name keeps its authored spelling; equality uses the
/// source's name rule so a Generals reference matches regardless of case.
/// </summary>
public readonly struct ContentId : IEquatable<ContentId>
{
    public ContentSource Source { get; }

    public ContentKind Kind { get; }

    public string Name { get; }

    public ContentId(ContentSource source, ContentKind kind, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        Ensure.NotNullOrWhiteSpace(name, "Content name must not be empty.");

        Source = source;
        Kind = kind;
        Name = name;
    }

    public bool Equals(ContentId other)
    {
        return Source is not null
            && Source.Equals(other.Source)
            && Kind == other.Kind
            && Source.NameComparer.Equals(Name, other.Name);
    }

    public override bool Equals(object? obj) => obj is ContentId other && Equals(other);

    public override int GetHashCode()
    {
        return Source is null ? 0 : HashCode.Combine(Source, Kind, Source.NameComparer.GetHashCode(Name));
    }

    public static bool operator ==(ContentId left, ContentId right) => left.Equals(right);

    public static bool operator !=(ContentId left, ContentId right) => !left.Equals(right);

    public override string ToString() => $"{Source}:{Kind}:{Name}";
}
