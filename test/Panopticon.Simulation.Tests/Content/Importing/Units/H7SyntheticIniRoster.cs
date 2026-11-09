#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Synthetic legacy Generals INI install root for the H7-02 unit round-trip suite (WI-H7-02).
/// Pure fixture, no game install: the tree mirrors the layout the convert-ini pipeline loads
/// (Armor/Locomotor/Weapon/Upgrade/Default Object plus an Object directory) and covers each
/// record kind the Generals adapter emits, one ChildObject inheritance chain, a prerequisite
/// dependency, and distinct damage types so field fidelity is observable.
/// </summary>
internal static class H7SyntheticIniRoster
{
    public const string DefaultTemplateName = "DefaultThingTemplate";

    public const string InfantryName = "SyntheticInfantry";

    public const string VehicleName = "SyntheticVehicle";

    public const string SupportCenterName = "SyntheticSupportCenter";

    public const string ArmorName = "SyntheticArmor";

    public const string RifleName = "SyntheticRifle";

    public const string CannonName = "SyntheticCannon";

    public const string LocomotorName = "SyntheticLocomotor";

    public const string UpgradeName = "SyntheticUpgrade";

    /// <summary>Unit roster roots the CLI is given; the pipeline prepends <see cref="DefaultTemplateName"/>.</summary>
    public static readonly string[] RosterRoots = [InfantryName, VehicleName];

    /// <summary>Every record the CLI roster closure must contain (the upgrade joins only the full-kind closure).</summary>
    public static readonly string[] ClosureRecordNames =
    [
        DefaultTemplateName,
        InfantryName,
        VehicleName,
        SupportCenterName,
        ArmorName,
        RifleName,
        CannonName,
        LocomotorName,
    ];

    private const string DefaultObjectIni =
        $$"""
        Object {{DefaultTemplateName}}
          Body = ActiveBody ModuleTag_DefaultBody
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryHeight = 5.0
        End
        """;

    private const string ObjectIni =
        $$"""
        Object {{InfantryName}}
          BuildCost = 225
          BuildTime = 5.0
          VisionRange = 150
          KindOf = INFANTRY SELECTABLE
          ArmorSet
            Conditions = None
            Armor = {{ArmorName}}
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY {{RifleName}}
            Weapon = SECONDARY None
          End
          Prerequisites
            Object = {{SupportCenterName}}
          End
          Body = ActiveBody ModuleTag_InfantryBody
            MaxHealth = 180.0
            InitialHealth = 180.0
          End
          Behavior = AIUpdateInterface ModuleTag_InfantryAI
          End
          Locomotor = SET_NORMAL {{LocomotorName}}
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryHeight = 12.0
        End

        ChildObject {{VehicleName}} {{InfantryName}}
          BuildCost = 800
          BuildTime = 8.0
          KindOf = VEHICLE SELECTABLE
          WeaponSet
            Conditions = None
            Weapon = PRIMARY {{CannonName}}
            Weapon = SECONDARY None
          End
          Geometry = CYLINDER
          GeometryMajorRadius = 14.0
          GeometryHeight = 10.0
        End

        Object {{SupportCenterName}}
          BuildCost = 2000
          BuildTime = 30.0
          VisionRange = 200
          KindOf = STRUCTURE SELECTABLE
          Body = ActiveBody ModuleTag_SupportBody
            MaxHealth = 1500.0
            InitialHealth = 1500.0
          End
          Geometry = CYLINDER
          GeometryMajorRadius = 30.0
          GeometryHeight = 40.0
        End
        """;

    private const string ArmorIni =
        $$"""
        Armor {{ArmorName}}
          Armor = DEFAULT 100%
          Armor = SMALL_ARMS 50%
          Armor = LASER 25%
        End
        """;

    private const string WeaponIni =
        $$"""
        Weapon {{RifleName}}
          PrimaryDamage = 5.0
          DamageType = SMALL_ARMS
          AttackRange = 100.0
          DelayBetweenShots = 500
        End

        Weapon {{CannonName}}
          PrimaryDamage = 40.0
          DamageType = EXPLOSION
          AttackRange = 175.0
          DelayBetweenShots = 2000
        End
        """;

    private const string LocomotorIni =
        $$"""
        Locomotor {{LocomotorName}}
          Surfaces = GROUND RUBBLE
          Speed = 30
          TurnRate = 180
          Appearance = TWO_LEGS
        End
        """;

    private const string UpgradeIni =
        $$"""
        Upgrade {{UpgradeName}}
          DisplayName = UPGRADE:SyntheticUpgrade
          Type = Player
          BuildCost = 500.0
          BuildTime = 10.0
        End
        """;

    /// <summary>Writes the synthetic install tree under <paramref name="root"/> and returns the root path.</summary>
    public static string WriteTo(string root)
    {
        WriteFile(Path.Combine(root, "Data", "INI", "Default", "Object.ini"), DefaultObjectIni);
        WriteFile(Path.Combine(root, "Data", "INI", "Armor.ini"), ArmorIni);
        WriteFile(Path.Combine(root, "Data", "INI", "Locomotor.ini"), LocomotorIni);
        WriteFile(Path.Combine(root, "Data", "INI", "Weapon.ini"), WeaponIni);
        WriteFile(Path.Combine(root, "Data", "INI", "Upgrade.ini"), UpgradeIni);
        WriteFile(Path.Combine(root, "Data", "INI", "Object", "Synthetic.ini"), ObjectIni);
        return root;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
