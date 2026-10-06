#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Units;

public sealed record ProjectileRecord : SourceRecord
{
    public ProjectileRecord(ContentId id, Provenance provenance)
        : base(id, ContentKind.Projectile, provenance)
    {
    }

    public Authored<Quantity> Speed { get; init; }

    public Authored<ContentId> Visual { get; init; }

    public override IEnumerable<ContentReference> GetReferences() => Reference(Visual, DependencyKind.Visual);

    internal override SourceRecord InheritFrom(SourceRecord? parent)
    {
        var p = parent as ProjectileRecord;

        return this with
        {
            Parent = null,
            Unclassified = MergeUnclassified(parent),
            Speed = Authored<Quantity>.Inherit(Speed.WithOrigin(Id), p?.Speed ?? default),
            Visual = Authored<ContentId>.Inherit(Visual.WithOrigin(Id), p?.Visual ?? default),
        };
    }
}
