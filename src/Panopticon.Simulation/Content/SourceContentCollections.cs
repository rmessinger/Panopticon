#nullable enable

using System;
using Panopticon.Content.Importing.Units;
using Panopticon.Logic.Object;

namespace Panopticon.Content;

/// <summary>
/// Unit content published by one non-Generals source (decision D2). Each source owns its own
/// scoped collections, so its names cannot collide with Generals built-ins or other sources;
/// the collections join the store's scope stack like every other collection.
/// </summary>
public sealed class SourceContentCollections
{
    public ContentSource Source { get; }

    public ScopedAssetCollection<ObjectDefinition> ObjectDefinitions { get; }

    public ScopedAssetCollection<WeaponTemplate> WeaponTemplates { get; }

    public ScopedAssetCollection<ArmorTemplate> ArmorTemplates { get; }

    public ScopedAssetCollection<LocomotorTemplate> LocomotorTemplates { get; }

    public ScopedAssetCollection<UpgradeTemplate> Upgrades { get; }

    internal SourceContentCollections(ContentSource source, AssetStore store)
    {
        Source = source;
        ObjectDefinitions = new ScopedAssetCollection<ObjectDefinition>(store);
        WeaponTemplates = new ScopedAssetCollection<WeaponTemplate>(store);
        ArmorTemplates = new ScopedAssetCollection<ArmorTemplate>(store);
        LocomotorTemplates = new ScopedAssetCollection<LocomotorTemplate>(store);
        Upgrades = new ScopedAssetCollection<UpgradeTemplate>(store);
    }

    internal IScopedAssetCollection[] All => [ObjectDefinitions, WeaponTemplates, ArmorTemplates, LocomotorTemplates, Upgrades];

    /// <summary>The collection that holds runtime assets for a content kind, or null for kinds without one.</summary>
    internal IScopedAssetCollection? For(ContentKind kind)
    {
        return kind switch
        {
            ContentKind.Unit => ObjectDefinitions,
            ContentKind.Weapon => WeaponTemplates,
            ContentKind.Armor => ArmorTemplates,
            ContentKind.Locomotor => LocomotorTemplates,
            ContentKind.Upgrade => Upgrades,
            _ => null,
        };
    }

    public BaseAsset? Find(ContentId id)
    {
        if (!id.Source.Equals(Source))
        {
            throw new ArgumentException($"'{id}' does not belong to source {Source}.", nameof(id));
        }

        return For(id.Kind)?.FindByName(id.Name);
    }
}
