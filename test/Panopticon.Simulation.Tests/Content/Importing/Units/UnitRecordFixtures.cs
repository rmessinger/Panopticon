using System.Collections.Generic;
using Panopticon.Content.Importing.Units;
using Panopticon.Logic.Object;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>Synthetic, source-neutral records for resolver tests; no install required.</summary>
internal static class UnitRecordFixtures
{
    public static readonly ContentSource Source = new("synthetic", caseSensitiveNames: false);

    public static ContentId Id(ContentKind kind, string name) => new(Source, kind, name);

    public static Provenance At(int line) => new(Source.Id, "synthetic.ini", line);

    public static Quantity Hp(double value) => new(value, QuantityUnit.HitPoints);

    public static Quantity Distance(double value) => new(value, QuantityUnit.GameDistance);

    public static Quantity Seconds(double value) => new(value, QuantityUnit.Seconds);

    public static Quantity Speed(double value) => new(value, QuantityUnit.DistancePerSecond);

    public static Quantity Money(double value) => new(value, QuantityUnit.Currency);

    public static UnitRecord Unit(string name, string? parent = null)
    {
        return new UnitRecord(Id(ContentKind.Unit, name), At(1))
        {
            Parent = parent is null ? null : Id(ContentKind.Unit, parent),
        };
    }

    public static WeaponRecord Weapon(string name)
    {
        return new WeaponRecord(Id(ContentKind.Weapon, name), At(2))
        {
            PrimaryDamage = Authored.Of(Hp(10)),
            AttackRange = Authored.Of(Distance(100)),
            ReloadTime = Authored.Of(Seconds(1)),
        };
    }

    public static LocomotorRecord Locomotor(string name)
    {
        return new LocomotorRecord(Id(ContentKind.Locomotor, name), At(3)) { Speed = Authored.Of(Speed(30)) };
    }

    public static ArmorRecord Armor(string name)
    {
        return new ArmorRecord(Id(ContentKind.Armor, name), At(4))
        {
            Multipliers = new Dictionary<string, Authored<double>> { ["SMALL_ARMS"] = Authored.Of(0.5) },
        };
    }

    public static UnitModule Body(string tag = "ModuleTag_Body") => new(tag, "ActiveBody", ModuleKinds.Body);

    /// <summary>A complete valid unit with weapon, armor, and locomotor.</summary>
    public static UnitContentSet ValidSoldier(params SourceRecord[] extra)
    {
        var unit = Unit("Soldier") with
        {
            MaxHealth = Authored.Of(Hp(100)),
            Weapons = ListOverride<ContentId>.Replace([Id(ContentKind.Weapon, "Rifle")]),
            Armor = Authored.Of(Id(ContentKind.Armor, "Flesh")),
            Locomotors = ListOverride<ContentId>.Replace([Id(ContentKind.Locomotor, "Feet")]),
            Modules = [ModuleEdit.Add(Body())],
        };

        var records = new List<SourceRecord> { unit, Weapon("Rifle"), Armor("Flesh"), Locomotor("Feet") };
        records.AddRange(extra);
        return new UnitContentSet(Source, records, [unit.Id]);
    }
}
