#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Content;
using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Card WI-H2a-19: parse-time side channel for Generals unit content. While active, the INI
/// loader registers unit blocks here instead of the store, so nothing reaches the store's
/// collections except through <see cref="Materialization.UnitContentPublisher.Publish"/>.
/// Duplicates are first-wins by instance id, mirroring
/// <see cref="ScopedAssetCollection{TAsset}.Add"/> at boot, and enumeration stays in parse order.
/// </summary>
public sealed class UnitContentStaging
{
    private readonly Dictionary<uint, ObjectDefinition> _objects = new();
    private readonly List<ObjectDefinition> _objectList = new();
    private readonly Dictionary<uint, WeaponTemplate> _weapons = new();
    private readonly List<WeaponTemplate> _weaponList = new();
    private readonly Dictionary<uint, ArmorTemplate> _armors = new();
    private readonly List<ArmorTemplate> _armorList = new();
    private readonly Dictionary<uint, LocomotorTemplate> _locomotors = new();
    private readonly List<LocomotorTemplate> _locomotorList = new();
    private readonly Dictionary<uint, UpgradeTemplate> _upgrades = new();
    private readonly List<UpgradeTemplate> _upgradeList = new();

    public IReadOnlyList<ObjectDefinition> Objects => _objectList;

    public IReadOnlyList<WeaponTemplate> Weapons => _weaponList;

    public IReadOnlyList<ArmorTemplate> Armors => _armorList;

    public IReadOnlyList<LocomotorTemplate> Locomotors => _locomotorList;

    public IReadOnlyList<UpgradeTemplate> Upgrades => _upgradeList;

    public int Count => _objectList.Count + _weaponList.Count + _armorList.Count + _locomotorList.Count + _upgradeList.Count;

    /// <summary>Resolves a staged object by name; null when the name is not staged.</summary>
    public ObjectDefinition? FindObject(string name)
    {
        return _objects.TryGetValue(AssetHash.GetHash(name), out var definition) ? definition : null;
    }

    internal void AddObject(ObjectDefinition definition) => Add(definition, _objects, _objectList);

    internal void AddWeapon(WeaponTemplate weapon) => Add(weapon, _weapons, _weaponList);

    internal void AddArmor(ArmorTemplate armor) => Add(armor, _armors, _armorList);

    internal void AddLocomotor(LocomotorTemplate locomotor) => Add(locomotor, _locomotors, _locomotorList);

    internal void AddUpgrade(UpgradeTemplate upgrade) => Add(upgrade, _upgrades, _upgradeList);

    private static void Add<TAsset>(TAsset asset, Dictionary<uint, TAsset> byInstanceId, List<TAsset> ordered)
        where TAsset : BaseAsset
    {
        if (byInstanceId.ContainsKey(asset.InstanceId))
        {
            return;
        }

        byInstanceId.Add(asset.InstanceId, asset);
        ordered.Add(asset);
    }
}
