#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// A unit ability. <see cref="Family"/> names the native capability it maps to (for example
/// <c>SpecialPower</c> or <c>Heal</c>); families outside the capability registry are unsupported.
/// </summary>
public sealed record AbilityRecord : SourceRecord
{
    public AbilityRecord(ContentId id, Provenance provenance)
        : base(id, ContentKind.Ability, provenance)
    {
    }

    public Authored<string> Family { get; init; }

    public Authored<Quantity> Range { get; init; }

    public Authored<Quantity> Cooldown { get; init; }

    public Authored<ContentId> Weapon { get; init; }

    public Authored<ContentId> RequiredUpgrade { get; init; }

    public override IEnumerable<ContentReference> GetReferences()
    {
        return Reference(Weapon, DependencyKind.Weapon).Concat(Reference(RequiredUpgrade, DependencyKind.Upgrade));
    }

    internal override SourceRecord InheritFrom(SourceRecord? parent)
    {
        var p = parent as AbilityRecord;

        return this with
        {
            Parent = null,
            Unclassified = MergeUnclassified(parent),
            Family = Authored<string>.Inherit(Family.WithOrigin(Id), p?.Family ?? default),
            Range = Authored<Quantity>.Inherit(Range.WithOrigin(Id), p?.Range ?? default),
            Cooldown = Authored<Quantity>.Inherit(Cooldown.WithOrigin(Id), p?.Cooldown ?? default),
            Weapon = Authored<ContentId>.Inherit(Weapon.WithOrigin(Id), p?.Weapon ?? default),
            RequiredUpgrade = Authored<ContentId>.Inherit(RequiredUpgrade.WithOrigin(Id), p?.RequiredUpgrade ?? default),
        };
    }
}
