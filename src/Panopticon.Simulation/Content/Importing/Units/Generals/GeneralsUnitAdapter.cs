#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Data.Ini;
using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units.Generals;

/// <summary>
/// Card WI-H2a-13: the Generals reference adapter. The INI parser still builds native definitions,
/// because child objects, reskins, and <c>DefaultThingTemplate</c> resolve their parents while
/// parsing. This adapter then describes every parsed object and dependency in engine-owned records:
/// typed fields for the capability matrix, plus legacy bindings to the native template and every
/// native module (decision D1). Typed values are read back from native data in declared source
/// units, so they are evidence of what the runtime holds rather than a second parse.
/// </summary>
public sealed class GeneralsUnitAdapter : IUnitSourceAdapter
{
    private static readonly Provenance NativeProvenance = new(ContentSource.Generals.Id, "INI");

    private readonly AssetStore _store;
    private readonly SageGame _game;
    private readonly GeneralsNativeTable _natives = new();

    public GeneralsUnitAdapter(AssetStore store, SageGame game)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _game = game;
    }

    public ContentSource Source => ContentSource.Generals;

    public Materialization.ILegacyNativeTable Natives => _natives;

    /// <summary>Every native module family the engine implements; legacy bindings are accepted only for these.</summary>
    public static IReadOnlyCollection<string> NativeModuleFamilies { get; } = typeof(ModuleData).Assembly.GetTypes()
        .Where(type => typeof(ModuleData).IsAssignableFrom(type) && !type.IsAbstract)
        .Select(FamilyName)
        .ToHashSet(StringComparer.Ordinal);

    public static UnitCapabilityRegistry CreateRegistry() => UnitCapabilityRegistry.CreateDefault(NativeModuleFamilies);

    /// <param name="rootNames">Object names to publish; empty means every parsed object.</param>
    public UnitContentSet Read(IReadOnlyCollection<string> rootNames)
    {
        ArgumentNullException.ThrowIfNull(rootNames);

        var records = new List<SourceRecord>();
        records.AddRange(((IEnumerable<WeaponTemplate>)_store.WeaponTemplates).Select(ReadWeapon));
        records.AddRange(((IEnumerable<ArmorTemplate>)_store.ArmorTemplates).Select(ReadArmor));
        records.AddRange(((IEnumerable<LocomotorTemplate>)_store.LocomotorTemplates).Select(ReadLocomotor));
        records.AddRange(((IEnumerable<UpgradeTemplate>)_store.Upgrades).Select(ReadUpgrade));

        var objects = ((IEnumerable<ObjectDefinition>)_store.ObjectDefinitions).ToList();
        records.AddRange(objects.Select(ReadObject));

        var roots = rootNames.Count == 0
            ? objects.Select(o => Id(ContentKind.Unit, o.Name))
            : rootNames.Select(name => Id(ContentKind.Unit, name));

        return new UnitContentSet(Source, records, roots);
    }

    internal static string FamilyName(Type moduleDataType)
    {
        const string Suffix = "ModuleData";
        var name = moduleDataType.Name;
        return name.EndsWith(Suffix, StringComparison.Ordinal) ? name[..^Suffix.Length] : name;
    }

    private static ContentId Id(ContentKind kind, string name) => new(ContentSource.Generals, kind, name);

    private double Fps => _game.LogicFramesPerSecond();

    private WeaponRecord ReadWeapon(WeaponTemplate weapon)
    {
        var damage = weapon.Nuggets.OfType<DamageNugget>().FirstOrDefault();
        var projectile = weapon.Nuggets.OfType<ProjectileNugget>().FirstOrDefault();

        return new WeaponRecord(Id(ContentKind.Weapon, weapon.Name), NativeProvenance)
        {
            Legacy = _natives.Bind(weapon),
            AttackRange = Authored.Of(new Quantity(weapon.AttackRange, QuantityUnit.GameDistance)),
            MinimumAttackRange = Authored.Of(new Quantity(weapon.MinimumAttackRange, QuantityUnit.GameDistance)),
            ReloadTime = Authored.Of(new Quantity(weapon.CoolDownDelayBetweenShots.Min.Value, QuantityUnit.LogicFrames)),
            ClipSize = Authored.Of(weapon.ClipSize),
            ClipReloadTime = Authored.Of(new Quantity(weapon.ClipReloadTime.Min.Value, QuantityUnit.LogicFrames)),
            PrimaryDamage = damage is null ? default : Authored.Of(new Quantity(damage.Damage, QuantityUnit.HitPoints)),
            PrimaryDamageRadius = damage is null ? default : Authored.Of(new Quantity(damage.Radius, QuantityUnit.GameDistance)),
            DamageType = damage is null ? default : Authored.Of(EnumName(damage.DamageType)),
            Presentation = projectile?.ProjectileTemplate?.Name is { } projectileName
                ? ListOverride<ContentId>.Replace([Id(ContentKind.Unit, projectileName)])
                : ListOverride<ContentId>.Inherit,
        };
    }

    private ArmorRecord ReadArmor(ArmorTemplate armor)
    {
        var multipliers = new Dictionary<string, Authored<double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var damageType in Enum.GetValues<DamageType>())
        {
            var index = (int)damageType;
            if (index < armor.Values.Length && EnumName(damageType) is { Length: > 0 } name)
            {
                multipliers[name] = Authored.Of((double)(1f * armor.Values[index]));
            }
        }

        return new ArmorRecord(Id(ContentKind.Armor, armor.Name), NativeProvenance)
        {
            Legacy = _natives.Bind(armor),
            Multipliers = multipliers,
        };
    }

    private LocomotorRecord ReadLocomotor(LocomotorTemplate locomotor)
    {
        return new LocomotorRecord(Id(ContentKind.Locomotor, locomotor.Name), NativeProvenance)
        {
            Legacy = _natives.Bind(locomotor),
            Speed = Authored.Of(new Quantity(locomotor.MaxSpeed * Fps, QuantityUnit.DistancePerSecond)),
            TurnRate = Authored.Of(new Quantity(locomotor.MaxTurnRate * Fps * 180.0 / Math.PI, QuantityUnit.DegreesPerSecond)),
            Acceleration = Authored.Of(new Quantity(locomotor.Acceleration * Fps * Fps, QuantityUnit.DistancePerSecondSquared)),
            Appearance = Authored.Of(EnumName(locomotor.Appearance)),
            Surfaces = ListOverride<string>.Replace(Enum.GetValues<Surfaces>()
                .Where(surface => surface != Surfaces.None && locomotor.Surfaces.HasFlag(surface))
                .Select(EnumName)),
        };
    }

    private UpgradeRecord ReadUpgrade(UpgradeTemplate upgrade)
    {
        return new UpgradeRecord(Id(ContentKind.Upgrade, upgrade.Name), NativeProvenance)
        {
            Legacy = _natives.Bind(upgrade),
            DisplayName = upgrade.DisplayName is null ? default : Authored.Of(upgrade.DisplayName),
            BuildCost = Authored.Of(new Quantity(upgrade.BuildCost, QuantityUnit.Currency)),
            BuildTime = Authored.Of(new Quantity(upgrade.BuildTime.Value, QuantityUnit.LogicFrames)),
            Scope = Authored.Of(upgrade.Type == UpgradeType.Object ? "Object" : "Player"),
        };
    }

    private UnitRecord ReadObject(ObjectDefinition definition)
    {
        var body = definition.Behaviors.Values.Select(m => m.Data).OfType<ActiveBodyModuleData>().FirstOrDefault();

        var weapons = definition.WeaponSets.Values
            .SelectMany(set => set.Slots)
            .Where(slot => slot?.Weapon?.Name is not null && !IsNoneSentinel(slot.Weapon.Name))
            .Select(slot => slot.Weapon.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => Id(ContentKind.Weapon, name));

        var locomotors = definition.LocomotorSets.Values
            .SelectMany(set => set.Locomotors ?? [])
            .Where(reference => reference?.Name is not null && !IsNoneSentinel(reference.Name))
            .Select(reference => reference.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => Id(ContentKind.Locomotor, name));

        var armor = definition.ArmorSets.Select(set => set.Armor?.Name).FirstOrDefault(name => name is not null && !IsNoneSentinel(name));

        var prerequisites = definition.Prerequisites?.Objects
            .SelectMany(list => list)
            .Where(reference => reference?.Name is not null)
            .Select(reference => Id(ContentKind.Unit, reference.Name))
            .Distinct() ?? [];

        var modules = definition.Behaviors.Values
            .Concat(definition.Draws.Values)
            .Concat(definition.ClientUpdates.Values)
            .Concat(definition.ClientBehaviors.Values)
            .Select(container => ModuleEdit.Add(new UnitModule(
                container.Tag,
                FamilyName(container.Data.GetType()),
                container.Data.ModuleKinds,
                container.InheritanceMode,
                Legacy: _natives.Bind(container.Data),
                Inherited: container.Inherited)));

        return new UnitRecord(Id(ContentKind.Unit, definition.Name), NativeProvenance)
        {
            Legacy = _natives.Bind(definition),
            DisplayName = definition.DisplayName is null ? default : Authored.Of(definition.DisplayName),
            MaxHealth = body is null ? default : Authored.Of(new Quantity(body.MaxHealth, QuantityUnit.HitPoints)),
            InitialHealth = body is null ? default : Authored.Of(new Quantity(body.InitialHealth, QuantityUnit.HitPoints)),
            GeometryMajorRadius = Authored.Of(new Quantity(definition.Geometry.MajorRadius, QuantityUnit.GameDistance)),
            GeometryHeight = Authored.Of(new Quantity(definition.Geometry.Shapes[0].Height, QuantityUnit.GameDistance)),
            VisionRange = Authored.Of(new Quantity(definition.VisionRange, QuantityUnit.GameDistance)),
            KindOf = ListOverride<string>.Replace(definition.KindOf.GetSetBits().Select(EnumName)),
            Weapons = ListOverride<ContentId>.Replace(weapons),
            Armor = armor is null ? default : Authored.Of(Id(ContentKind.Armor, armor)),
            Locomotors = ListOverride<ContentId>.Replace(locomotors),
            BuildCost = Authored.Of(new Quantity(definition.BuildCost, QuantityUnit.Currency)),
            BuildTime = Authored.Of(new Quantity(definition.BuildTime.Value, QuantityUnit.LogicFrames)),
            Prerequisites = ListOverride<ContentId>.Replace(prerequisites),
            Modules = modules.ToArray(),
        };
    }

    /// <summary>Generals INI writes <c>None</c> for an empty weapon slot or armor set; the native lazy reference resolves to null.</summary>
    private static bool IsNoneSentinel(string name) => string.Equals(name, "None", StringComparison.OrdinalIgnoreCase);

    private static string EnumName<T>(T value)
        where T : Enum
    {
        return IniParser.GetEnumMapReverse<T>().TryGetValue(value, out var name) ? name : value.ToString();
    }
}
