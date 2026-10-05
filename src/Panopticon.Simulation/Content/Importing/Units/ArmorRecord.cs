#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

/// <summary>Damage-type multipliers. A child's entries override the parent's per damage type.</summary>
public sealed record ArmorRecord : SourceRecord
{
    private readonly IReadOnlyDictionary<string, Authored<double>> _multipliers = new Dictionary<string, Authored<double>>();

    public ArmorRecord(ContentId id, Provenance provenance)
        : base(id, ContentKind.Armor, provenance)
    {
    }

    /// <summary>Damage multiplier per damage type, where 1 is full damage.</summary>
    public IReadOnlyDictionary<string, Authored<double>> Multipliers
    {
        get => _multipliers;
        init => _multipliers = new Dictionary<string, Authored<double>>(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Multiplier for damage types without an entry.</summary>
    public Authored<double> DefaultMultiplier { get; init; }

    public override IEnumerable<ContentReference> GetReferences() => [];

    internal override SourceRecord InheritFrom(SourceRecord? parent)
    {
        var p = parent as ArmorRecord;
        var merged = new Dictionary<string, Authored<double>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (damageType, multiplier) in p?.Multipliers ?? new Dictionary<string, Authored<double>>())
        {
            merged[damageType] = multiplier;
        }

        foreach (var (damageType, multiplier) in Multipliers.Where(pair => pair.Value.IsPresent))
        {
            merged[damageType] = multiplier.WithOrigin(Id);
        }

        return this with
        {
            Parent = null,
            Unclassified = MergeUnclassified(parent),
            Multipliers = merged,
            DefaultMultiplier = Authored<double>.Inherit(DefaultMultiplier.WithOrigin(Id), p?.DefaultMultiplier ?? default),
        };
    }
}
