#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Base of every engine-owned unit-content record an adapter emits. Records are immutable values;
/// collection properties copy their input.
/// </summary>
public abstract record SourceRecord
{
    private readonly IReadOnlyList<UnclassifiedField> _unclassified = [];

    public ContentId Id { get; }

    public Provenance Provenance { get; }

    /// <summary>Parent record of the same kind; null after inheritance resolution.</summary>
    public ContentId? Parent { get; init; }

    public IReadOnlyList<UnclassifiedField> Unclassified
    {
        get => _unclassified;
        init => _unclassified = value.ToArray();
    }

    protected SourceRecord(ContentId id, ContentKind expectedKind, Provenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        if (id.Kind != expectedKind)
        {
            throw new ArgumentException($"Record id '{id}' must have kind {expectedKind}.", nameof(id));
        }

        Id = id;
        Provenance = provenance;
    }

    /// <summary>Typed edges to other records, excluding <see cref="Parent"/>.</summary>
    public abstract IEnumerable<ContentReference> GetReferences();

    /// <summary>
    /// Returns this record merged over an already-resolved parent of the same type (or over nothing),
    /// with origins stamped and <see cref="Parent"/> cleared.
    /// </summary>
    internal abstract SourceRecord InheritFrom(SourceRecord? parent);

    private protected IReadOnlyList<UnclassifiedField> MergeUnclassified(SourceRecord? parent)
    {
        return parent is null ? Unclassified : parent.Unclassified.Concat(Unclassified).ToArray();
    }

    private protected static IEnumerable<ContentReference> References(ListOverride<ContentId> list, DependencyKind kind)
    {
        return list.Items.Select(id => new ContentReference(id, kind));
    }

    private protected static IEnumerable<ContentReference> Reference(Authored<ContentId> value, DependencyKind kind)
    {
        return value.IsPresent ? [new ContentReference(value.Value, kind)] : [];
    }
}
