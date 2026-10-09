#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

/// <summary>Direct or projectile weapon. A present <see cref="Projectile"/> makes it a projectile weapon.</summary>
public sealed record WeaponRecord : SourceRecord
{
    public WeaponRecord(ContentId id, Provenance provenance)
        : base(id, ContentKind.Weapon, provenance)
    {
    }

    public Authored<Quantity> PrimaryDamage { get; init; }

    public Authored<Quantity> PrimaryDamageRadius { get; init; }

    public Authored<string> DamageType { get; init; }

    public Authored<Quantity> AttackRange { get; init; }

    public Authored<Quantity> MinimumAttackRange { get; init; }

    /// <summary>Time between shots.</summary>
    public Authored<Quantity> ReloadTime { get; init; }

    /// <summary>Delay before the first shot of an attack (EE2 <c>applyDamageTime</c>).</summary>
    public Authored<Quantity> PreAttackDelay { get; init; }

    public Authored<int> ClipSize { get; init; }

    public Authored<Quantity> ClipReloadTime { get; init; }

    /// <summary>Target categories the weapon may engage, for example <c>Ground</c> and <c>Air</c>.</summary>
    public ListOverride<string> TargetCategories { get; init; } = ListOverride<string>.Inherit;

    public Authored<ContentId> Projectile { get; init; }

    public ListOverride<ContentId> Presentation { get; init; } = ListOverride<ContentId>.Inherit;

    public override IEnumerable<ContentReference> GetReferences()
    {
        return Reference(Projectile, DependencyKind.Projectile).Concat(References(Presentation, DependencyKind.Presentation));
    }

    internal override SourceRecord InheritFrom(SourceRecord? parent)
    {
        var p = parent as WeaponRecord;

        return this with
        {
            Parent = null,
            Unclassified = MergeUnclassified(parent),
            PrimaryDamage = Authored<Quantity>.Inherit(PrimaryDamage.WithOrigin(Id), p?.PrimaryDamage ?? default),
            PrimaryDamageRadius = Authored<Quantity>.Inherit(PrimaryDamageRadius.WithOrigin(Id), p?.PrimaryDamageRadius ?? default),
            DamageType = Authored<string>.Inherit(DamageType.WithOrigin(Id), p?.DamageType ?? default),
            AttackRange = Authored<Quantity>.Inherit(AttackRange.WithOrigin(Id), p?.AttackRange ?? default),
            MinimumAttackRange = Authored<Quantity>.Inherit(MinimumAttackRange.WithOrigin(Id), p?.MinimumAttackRange ?? default),
            ReloadTime = Authored<Quantity>.Inherit(ReloadTime.WithOrigin(Id), p?.ReloadTime ?? default),
            PreAttackDelay = Authored<Quantity>.Inherit(PreAttackDelay.WithOrigin(Id), p?.PreAttackDelay ?? default),
            ClipSize = Authored<int>.Inherit(ClipSize.WithOrigin(Id), p?.ClipSize ?? default),
            ClipReloadTime = Authored<Quantity>.Inherit(ClipReloadTime.WithOrigin(Id), p?.ClipReloadTime ?? default),
            TargetCategories = TargetCategories.ApplyOver(p?.TargetCategories ?? ListOverride<string>.Inherit, Id.Source.NameComparer),
            Projectile = Authored<ContentId>.Inherit(Projectile.WithOrigin(Id), p?.Projectile ?? default),
            Presentation = Presentation.ApplyOver(p?.Presentation ?? ListOverride<ContentId>.Inherit),
        };
    }
}
