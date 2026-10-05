#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

/// <summary>Gameplay and presentation description of one unit or structure.</summary>
public sealed record UnitRecord : SourceRecord
{
    private readonly IReadOnlyList<ModuleEdit> _modules = [];

    public UnitRecord(ContentId id, Provenance provenance)
        : base(id, ContentKind.Unit, provenance)
    {
    }

    public Authored<string> DisplayName { get; init; }

    public Authored<Quantity> MaxHealth { get; init; }

    public Authored<Quantity> InitialHealth { get; init; }

    public Authored<Quantity> GeometryMajorRadius { get; init; }

    public Authored<Quantity> GeometryHeight { get; init; }

    public Authored<Quantity> VisionRange { get; init; }

    public ListOverride<string> KindOf { get; init; } = ListOverride<string>.Inherit;

    public ListOverride<ContentId> Locomotors { get; init; } = ListOverride<ContentId>.Inherit;

    /// <summary>Weapons in slot order: primary, secondary, tertiary.</summary>
    public ListOverride<ContentId> Weapons { get; init; } = ListOverride<ContentId>.Inherit;

    public Authored<ContentId> Armor { get; init; }

    public Authored<Quantity> BuildCost { get; init; }

    public Authored<Quantity> BuildTime { get; init; }

    public ListOverride<ContentId> Prerequisites { get; init; } = ListOverride<ContentId>.Inherit;

    public ListOverride<ContentId> Produces { get; init; } = ListOverride<ContentId>.Inherit;

    public ListOverride<ContentId> Upgrades { get; init; } = ListOverride<ContentId>.Inherit;

    public Authored<ContentId> Visual { get; init; }

    public ListOverride<ContentId> Presentation { get; init; } = ListOverride<ContentId>.Inherit;

    /// <summary>Ordered module edits; after resolution, one <see cref="ModuleEditOperation.Add"/> per surviving module.</summary>
    public IReadOnlyList<ModuleEdit> Modules
    {
        get => _modules;
        init => _modules = value.ToArray();
    }

    /// <summary>Resolved modules; only meaningful after inheritance resolution.</summary>
    public IEnumerable<UnitModule> ResolvedModules => Modules.Where(edit => edit.Module is not null).Select(edit => edit.Module!);

    public override IEnumerable<ContentReference> GetReferences()
    {
        return References(Locomotors, DependencyKind.Locomotor)
            .Concat(References(Weapons, DependencyKind.Weapon))
            .Concat(Reference(Armor, DependencyKind.Armor))
            .Concat(References(Prerequisites, DependencyKind.Prerequisite))
            .Concat(References(Produces, DependencyKind.Production))
            .Concat(References(Upgrades, DependencyKind.Upgrade))
            .Concat(Reference(Visual, DependencyKind.Visual))
            .Concat(References(Presentation, DependencyKind.Presentation))
            .Concat(Modules
                .Where(edit => edit.Module?.Ability is not null)
                .Select(edit => new ContentReference(edit.Module!.Ability!.Value, DependencyKind.Ability)));
    }

    internal override SourceRecord InheritFrom(SourceRecord? parent)
    {
        var p = parent as UnitRecord;
        return this with
        {
            Parent = null,
            Unclassified = MergeUnclassified(parent),
            DisplayName = Authored<string>.Inherit(DisplayName.WithOrigin(Id), p?.DisplayName ?? default),
            MaxHealth = Authored<Quantity>.Inherit(MaxHealth.WithOrigin(Id), p?.MaxHealth ?? default),
            InitialHealth = Authored<Quantity>.Inherit(InitialHealth.WithOrigin(Id), p?.InitialHealth ?? default),
            GeometryMajorRadius = Authored<Quantity>.Inherit(GeometryMajorRadius.WithOrigin(Id), p?.GeometryMajorRadius ?? default),
            GeometryHeight = Authored<Quantity>.Inherit(GeometryHeight.WithOrigin(Id), p?.GeometryHeight ?? default),
            VisionRange = Authored<Quantity>.Inherit(VisionRange.WithOrigin(Id), p?.VisionRange ?? default),
            KindOf = KindOf.ApplyOver(p?.KindOf ?? ListOverride<string>.Inherit, Id.Source.NameComparer),
            Locomotors = Locomotors.ApplyOver(p?.Locomotors ?? ListOverride<ContentId>.Inherit),
            Weapons = Weapons.ApplyOver(p?.Weapons ?? ListOverride<ContentId>.Inherit),
            Armor = Authored<ContentId>.Inherit(Armor.WithOrigin(Id), p?.Armor ?? default),
            BuildCost = Authored<Quantity>.Inherit(BuildCost.WithOrigin(Id), p?.BuildCost ?? default),
            BuildTime = Authored<Quantity>.Inherit(BuildTime.WithOrigin(Id), p?.BuildTime ?? default),
            Prerequisites = Prerequisites.ApplyOver(p?.Prerequisites ?? ListOverride<ContentId>.Inherit),
            Produces = Produces.ApplyOver(p?.Produces ?? ListOverride<ContentId>.Inherit),
            Upgrades = Upgrades.ApplyOver(p?.Upgrades ?? ListOverride<ContentId>.Inherit),
            Visual = Authored<ContentId>.Inherit(Visual.WithOrigin(Id), p?.Visual ?? default),
            Presentation = Presentation.ApplyOver(p?.Presentation ?? ListOverride<ContentId>.Inherit),
            Modules = ModuleMerger.Merge(p?.ResolvedModules ?? [], Modules),
        };
    }
}
