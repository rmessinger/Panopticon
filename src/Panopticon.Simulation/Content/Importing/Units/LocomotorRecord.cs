#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Units;

public sealed record LocomotorRecord : SourceRecord
{
    public LocomotorRecord(ContentId id, Provenance provenance)
        : base(id, ContentKind.Locomotor, provenance)
    {
    }

    public Authored<Quantity> Speed { get; init; }

    public Authored<Quantity> TurnRate { get; init; }

    public Authored<Quantity> Acceleration { get; init; }

    /// <summary>Surfaces the locomotor may cross, for example <c>Ground</c>, <c>Water</c>, <c>Air</c>.</summary>
    public ListOverride<string> Surfaces { get; init; } = ListOverride<string>.Inherit;

    public override IEnumerable<ContentReference> GetReferences() => [];

    internal override SourceRecord InheritFrom(SourceRecord? parent)
    {
        var p = parent as LocomotorRecord;

        return this with
        {
            Parent = null,
            Unclassified = MergeUnclassified(parent),
            Speed = Authored<Quantity>.Inherit(Speed.WithOrigin(Id), p?.Speed ?? default),
            TurnRate = Authored<Quantity>.Inherit(TurnRate.WithOrigin(Id), p?.TurnRate ?? default),
            Acceleration = Authored<Quantity>.Inherit(Acceleration.WithOrigin(Id), p?.Acceleration ?? default),
            Surfaces = Surfaces.ApplyOver(p?.Surfaces ?? ListOverride<string>.Inherit, Id.Source.NameComparer),
        };
    }
}
