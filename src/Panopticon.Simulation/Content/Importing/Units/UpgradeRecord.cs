#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Units;

public sealed record UpgradeRecord : SourceRecord
{
    public UpgradeRecord(ContentId id, Provenance provenance)
        : base(id, ContentKind.Upgrade, provenance)
    {
    }

    public Authored<string> DisplayName { get; init; }

    public Authored<Quantity> BuildCost { get; init; }

    public Authored<Quantity> BuildTime { get; init; }

    /// <summary><c>Player</c> or <c>Object</c>, matching the native upgrade types.</summary>
    public Authored<string> Scope { get; init; }

    public ListOverride<ContentId> Prerequisites { get; init; } = ListOverride<ContentId>.Inherit;

    public override IEnumerable<ContentReference> GetReferences() => References(Prerequisites, DependencyKind.Prerequisite);

    internal override SourceRecord InheritFrom(SourceRecord? parent)
    {
        var p = parent as UpgradeRecord;

        return this with
        {
            Parent = null,
            Unclassified = MergeUnclassified(parent),
            DisplayName = Authored<string>.Inherit(DisplayName.WithOrigin(Id), p?.DisplayName ?? default),
            BuildCost = Authored<Quantity>.Inherit(BuildCost.WithOrigin(Id), p?.BuildCost ?? default),
            BuildTime = Authored<Quantity>.Inherit(BuildTime.WithOrigin(Id), p?.BuildTime ?? default),
            Scope = Authored<string>.Inherit(Scope.WithOrigin(Id), p?.Scope ?? default),
            Prerequisites = Prerequisites.ApplyOver(p?.Prerequisites ?? ListOverride<ContentId>.Inherit),
        };
    }
}
