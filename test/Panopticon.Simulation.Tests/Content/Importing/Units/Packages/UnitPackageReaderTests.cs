#nullable enable

using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

public sealed class UnitPackageReaderTests
{
    private const string RetainedIniContent =
        """
        Armor InfantryArmor
          Armor = DEFAULT 100%
          Armor = SMALL_ARMS 50%
        End

        Locomotor BasicHumanLocomotor
          Surfaces = GROUND RUBBLE
          Speed = 30
          TurnRate = 180
          Appearance = TWO_LEGS
        End

        Weapon RangerRifle
          PrimaryDamage = 5.0
          DamageType = SMALL_ARMS
          AttackRange = 100.0
          DelayBetweenShots = 500
        End

        Object Ranger
          BuildCost = 225
          BuildTime = 5.0
          VisionRange = 150
          KindOf = INFANTRY SELECTABLE
          ArmorSet
            Conditions = None
            Armor = InfantryArmor
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY RangerRifle
          End
          Body = ActiveBody ModuleTag_Body
            MaxHealth = 180.0
            InitialHealth = 180.0
          End
          Behavior = AIUpdateInterface ModuleTag_AI
          End
          Locomotor = SET_NORMAL BasicHumanLocomotor
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryHeight = 12.0
        End
        """;

    [Fact]
    public void ReadPackageWithAllRecordKindsAndRetainedIniSucceedsFieldByField()
    {
        using var builder = new UnitPackageTestBuilder();

        builder.AddRetainedPayload(
            "retained/generals.ini",
            UnitPackageFormat.SupportedGeneralsIniDialect,
            UnitPackageFormat.SupportedGeneralsIniDialectVersion,
            RetainedIniContent,
            new UnitPackageProvenance("generals", "Data/INI/Test.ini", Line: 1));

        builder.AddRoot("Ranger", ContentKind.Unit);

        var unitJson =
            """
            {
              "name": "Ranger",
              "kind": "Unit",
              "hasLegacyBinding": true,
              "displayName": "Ranger",
              "maxHealth": { "value": 180.0, "unit": "HitPoints" },
              "initialHealth": { "value": 180.0, "unit": "HitPoints" },
              "geometryMajorRadius": { "value": 10.0, "unit": "GameDistance" },
              "geometryHeight": { "value": 12.0, "unit": "GameDistance" },
              "visionRange": { "value": 150.0, "unit": "GameDistance" },
              "deathDuration": { "value": 30.0, "unit": "LogicFrames" },
              "kindOf": ["INFANTRY", "SELECTABLE"],
              "locomotors": [{ "name": "BasicHumanLocomotor", "kind": "Locomotor" }],
              "weapons": [{ "name": "RangerRifle", "kind": "Weapon" }],
              "armor": { "name": "InfantryArmor", "kind": "Armor" },
              "buildCost": { "value": 225.0, "unit": "Currency" },
              "buildTime": { "value": 5.0, "unit": "Seconds" },
              "prerequisites": [],
              "produces": [],
              "upgrades": [{ "name": "RangerFlashbang", "kind": "Upgrade" }],
              "visual": { "name": "RangerVisual", "kind": "VisualState" },
              "presentation": [],
              "modules": [
                {
                  "operation": "Add",
                  "tag": "ModuleTag_Body",
                  "module": {
                    "tag": "ModuleTag_Body",
                    "family": "ActiveBody",
                    "hasLegacyBinding": true
                  }
                },
                {
                  "operation": "Add",
                  "tag": "ModuleTag_AI",
                  "module": {
                    "tag": "ModuleTag_AI",
                    "family": "AIUpdateInterface",
                    "hasLegacyBinding": true
                  }
                }
              ],
              "unclassified": [
                {
                  "name": "CustomField",
                  "affectsGameplay": false,
                  "rawValue": "VerbatimCustomValue",
                  "rawBlock": ["line 1", "line 2"]
                }
              ]
            }
            """;

        var weaponJson =
            """
            {
              "name": "RangerRifle",
              "kind": "Weapon",
              "hasLegacyBinding": true,
              "primaryDamage": { "value": 5.0, "unit": "HitPoints" },
              "primaryDamageRadius": { "value": 0.0, "unit": "GameDistance" },
              "damageType": "SMALL_ARMS",
              "attackRange": { "value": 100.0, "unit": "GameDistance" },
              "minimumAttackRange": { "value": 0.0, "unit": "GameDistance" },
              "reloadTime": { "value": 500.0, "unit": "Milliseconds" },
              "preAttackDelay": { "value": 0.0, "unit": "Milliseconds" },
              "clipSize": 0,
              "clipReloadTime": { "value": 0.0, "unit": "LogicFrames" },
              "targetCategories": ["Ground"],
              "projectile": { "name": "BulletProjectile", "kind": "Projectile" }
            }
            """;

        var armorJson =
            """
            {
              "name": "InfantryArmor",
              "kind": "Armor",
              "hasLegacyBinding": true,
              "multipliers": {
                "DEFAULT": 1.0,
                "SMALL_ARMS": 0.5
              },
              "defaultMultiplier": 1.0
            }
            """;

        var locomotorJson =
            """
            {
              "name": "BasicHumanLocomotor",
              "kind": "Locomotor",
              "hasLegacyBinding": true,
              "speed": { "value": 30.0, "unit": "DistancePerSecond" },
              "turnRate": { "value": 180.0, "unit": "DegreesPerSecond" },
              "acceleration": { "value": 60.0, "unit": "DistancePerSecondSquared" },
              "braking": { "value": 60.0, "unit": "DistancePerSecondSquared" },
              "preferredHeight": { "value": 0.0, "unit": "GameDistance" },
              "canHover": false,
              "appearance": "TWO_LEGS",
              "surfaces": ["GROUND", "RUBBLE"]
            }
            """;

        var upgradeJson =
            """
            {
              "name": "RangerFlashbang",
              "kind": "Upgrade",
              "displayName": "Flashbang Grenades",
              "buildCost": { "value": 500.0, "unit": "Currency" },
              "buildTime": { "value": 30.0, "unit": "Seconds" },
              "scope": "Player",
              "prerequisites": []
            }
            """;

        var abilityJson =
            """
            {
              "name": "FlashbangAttack",
              "kind": "Ability",
              "family": "SpecialPower",
              "range": { "value": 120.0, "unit": "GameDistance" },
              "cooldown": { "value": 15.0, "unit": "Seconds" },
              "weapon": { "name": "RangerRifle", "kind": "Weapon" },
              "requiredUpgrade": { "name": "RangerFlashbang", "kind": "Upgrade" }
            }
            """;

        var projectileJson =
            """
            {
              "name": "BulletProjectile",
              "kind": "Projectile",
              "speed": { "value": 300.0, "unit": "DistancePerSecond" },
              "visual": { "name": "RangerVisual", "kind": "VisualState" }
            }
            """;

        var visualJson =
            """
            {
              "name": "RangerVisual",
              "kind": "VisualState",
              "modelKey": "Data/W3D/AIRngr.w3d",
              "stateAnimations": {
                "Idle": "AIRngr_IDLA",
                "Move": "AIRngr_WKA",
                "Attack": "AIRngr_ATA",
                "Death": "AIRngr_DTA"
              }
            }
            """;

        builder.AddRecord("Ranger", ContentKind.Unit, "records/units/ranger.json", unitJson);
        builder.AddRecord("RangerRifle", ContentKind.Weapon, "records/weapons/ranger_rifle.json", weaponJson);
        builder.AddRecord("InfantryArmor", ContentKind.Armor, "records/armors/infantry_armor.json", armorJson);
        builder.AddRecord("BasicHumanLocomotor", ContentKind.Locomotor, "records/locomotors/human_locomotor.json", locomotorJson);
        builder.AddRecord("RangerFlashbang", ContentKind.Upgrade, "records/upgrades/flashbang.json", upgradeJson);
        builder.AddRecord("FlashbangAttack", ContentKind.Ability, "records/abilities/flashbang_attack.json", abilityJson);
        builder.AddRecord("BulletProjectile", ContentKind.Projectile, "records/projectiles/bullet.json", projectileJson);
        builder.AddRecord("RangerVisual", ContentKind.VisualState, "records/visuals/ranger_visual.json", visualJson);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        Assert.Equal(ContentSource.Generals, reader.Source);
        Assert.NotNull(reader.PackageRoot);

        var set = reader.Read([]);

        Assert.False(set.Diagnostics.Any(d => d.IsError), $"Unexpected errors: {string.Join("; ", set.Diagnostics.Where(d => d.IsError))}");
        Assert.Equal(8, set.Records.Count);
        Assert.Single(set.Roots);
        Assert.Equal(new ContentId(ContentSource.Generals, ContentKind.Unit, "Ranger"), set.Roots[0]);

        // Assert UnitRecord
        var unit = set.Records.OfType<UnitRecord>().Single(r => r.Id.Name == "Ranger");
        Assert.True(unit.DisplayName.IsPresent);
        Assert.Equal("Ranger", unit.DisplayName.Value);
        Assert.Equal(180.0, unit.MaxHealth.Value.Value);
        Assert.Equal(QuantityUnit.HitPoints, unit.MaxHealth.Value.Unit);
        Assert.Equal(150.0, unit.VisionRange.Value.Value);
        Assert.Equal(["INFANTRY", "SELECTABLE"], unit.KindOf.Items);
        Assert.Single(unit.Locomotors.Items);
        Assert.Equal("BasicHumanLocomotor", unit.Locomotors.Items[0].Name);
        Assert.Single(unit.Weapons.Items);
        Assert.Equal("RangerRifle", unit.Weapons.Items[0].Name);
        Assert.Equal("InfantryArmor", unit.Armor.Value.Name);
        Assert.Equal(225.0, unit.BuildCost.Value.Value);
        Assert.Equal(QuantityUnit.Currency, unit.BuildCost.Value.Unit);

        // Assert Valued UnclassifiedField
        var unclassified = Assert.Single(unit.Unclassified);
        Assert.Equal("CustomField", unclassified.Name);
        Assert.False(unclassified.AffectsGameplay);
        Assert.Equal("VerbatimCustomValue", unclassified.RawValue);
        Assert.NotNull(unclassified.RawBlock);
        Assert.Equal(["line 1", "line 2"], unclassified.RawBlock);

        // Assert re-issued legacy bindings and staging template equality
        Assert.NotNull(unit.Legacy);
        Assert.NotNull(reader.Natives);

        var stagedObject = reader.Natives.Get(unit.Legacy.Handle) as ObjectDefinition;
        Assert.NotNull(stagedObject);
        Assert.Equal("Ranger", stagedObject.Name);
        Assert.Equal(225, stagedObject.BuildCost);

        // Assert module legacy bindings
        Assert.Equal(2, unit.Modules.Count);
        var bodyModule = unit.Modules.Single(m => m.Tag == "ModuleTag_Body").Module;
        Assert.NotNull(bodyModule);
        Assert.NotNull(bodyModule.Legacy);
        var stagedBodyData = reader.Natives.Get(bodyModule.Legacy.Handle);
        Assert.NotNull(stagedBodyData);
        Assert.Equal("ActiveBodyModuleData", stagedBodyData.GetType().Name);

        // Assert WeaponRecord
        var weapon = set.Records.OfType<WeaponRecord>().Single(r => r.Id.Name == "RangerRifle");
        Assert.NotNull(weapon.Legacy);
        Assert.Equal(5.0, weapon.PrimaryDamage.Value.Value);
        Assert.Equal("SMALL_ARMS", weapon.DamageType.Value);
        Assert.Equal(100.0, weapon.AttackRange.Value.Value);
        Assert.Equal(500.0, weapon.ReloadTime.Value.Value);
        Assert.Equal(QuantityUnit.Milliseconds, weapon.ReloadTime.Value.Unit);
        Assert.Equal("BulletProjectile", weapon.Projectile.Value.Name);
        var stagedWeapon = reader.Natives.Get(weapon.Legacy.Handle) as WeaponTemplate;
        Assert.NotNull(stagedWeapon);
        Assert.Equal("RangerRifle", stagedWeapon.Name);

        // Assert ArmorRecord
        var armor = set.Records.OfType<ArmorRecord>().Single(r => r.Id.Name == "InfantryArmor");
        Assert.NotNull(armor.Legacy);
        Assert.Equal(2, armor.Multipliers.Count);
        Assert.Equal(1.0, armor.Multipliers["DEFAULT"].Value);
        Assert.Equal(0.5, armor.Multipliers["SMALL_ARMS"].Value);
        var stagedArmor = reader.Natives.Get(armor.Legacy.Handle) as ArmorTemplate;
        Assert.NotNull(stagedArmor);
        Assert.Equal("InfantryArmor", stagedArmor.Name);

        // Assert LocomotorRecord
        var locomotor = set.Records.OfType<LocomotorRecord>().Single(r => r.Id.Name == "BasicHumanLocomotor");
        Assert.NotNull(locomotor.Legacy);
        Assert.Equal(30.0, locomotor.Speed.Value.Value);
        Assert.Equal("TWO_LEGS", locomotor.Appearance.Value);
        Assert.Equal(["GROUND", "RUBBLE"], locomotor.Surfaces.Items);
        var stagedLocomotor = reader.Natives.Get(locomotor.Legacy.Handle) as LocomotorTemplate;
        Assert.NotNull(stagedLocomotor);
        Assert.Equal("BasicHumanLocomotor", stagedLocomotor.Name);

        // Assert UpgradeRecord
        var upgrade = set.Records.OfType<UpgradeRecord>().Single(r => r.Id.Name == "RangerFlashbang");
        Assert.Null(upgrade.Legacy);
        Assert.Equal("Flashbang Grenades", upgrade.DisplayName.Value);
        Assert.Equal(500.0, upgrade.BuildCost.Value.Value);
        Assert.Equal("Player", upgrade.Scope.Value);

        // Assert AbilityRecord
        var ability = set.Records.OfType<AbilityRecord>().Single(r => r.Id.Name == "FlashbangAttack");
        Assert.Equal("SpecialPower", ability.Family.Value);
        Assert.Equal(120.0, ability.Range.Value.Value);
        Assert.Equal("RangerRifle", ability.Weapon.Value.Name);
        Assert.Equal("RangerFlashbang", ability.RequiredUpgrade.Value.Name);

        // Assert ProjectileRecord
        var projectile = set.Records.OfType<ProjectileRecord>().Single(r => r.Id.Name == "BulletProjectile");
        Assert.Equal(300.0, projectile.Speed.Value.Value);
        Assert.Equal("RangerVisual", projectile.Visual.Value.Name);

        // Assert VisualStateRecord
        var visual = set.Records.OfType<VisualStateRecord>().Single(r => r.Id.Name == "RangerVisual");
        Assert.Equal("Data/W3D/AIRngr.w3d", visual.ModelKey.Value);
        Assert.Equal(4, visual.StateAnimations.Count);
        Assert.Equal("AIRngr_ATA", visual.StateAnimations["Attack"].Value);
    }

    [Fact]
    public void ReadWithExplicitRootsFiltersAndResolvesCorrectly()
    {
        using var builder = new UnitPackageTestBuilder();

        builder.AddRecord("UnitA", ContentKind.Unit, "records/unita.json",
            """
            {
              "name": "UnitA",
              "kind": "Unit",
              "displayName": "Unit A"
            }
            """);

        builder.AddRecord("UnitB", ContentKind.Unit, "records/unitb.json",
            """
            {
              "name": "UnitB",
              "kind": "Unit",
              "displayName": "Unit B"
            }
            """);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        var set = reader.Read(["UnitA"]);

        Assert.DoesNotContain(set.Diagnostics, d => d.IsError);
        Assert.Equal(2, set.Records.Count);
        Assert.Single(set.Roots);
        Assert.Equal("UnitA", set.Roots[0].Name);
    }

    [Fact]
    public void ReadWithMissingRequestedRootReportsDiagnosticError()
    {
        using var builder = new UnitPackageTestBuilder();

        builder.AddRecord("UnitA", ContentKind.Unit, "records/unita.json",
            """
            {
              "name": "UnitA",
              "kind": "Unit"
            }
            """);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        var set = reader.Read(["MissingUnit"]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var diag = set.Diagnostics.First(d => d.IsError);
        Assert.Equal(DiagnosticCodes.MissingRoot, diag.Code);
        Assert.Empty(set.Records);
    }

    [Fact]
    public void ReadNonLegacyPurePackageWithoutRetainedPayloadsSucceeds()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.WithSource("ee2", caseSensitive: true);

        builder.AddRecord("Citizen", ContentKind.Unit, "records/citizen.json",
            """
            {
              "name": "Citizen",
              "kind": "Unit",
              "displayName": "Citizen",
              "maxHealth": { "value": 50.0, "unit": "HitPoints" },
              "buildCost": { "value": 100.0, "unit": "Currency" }
            }
            """);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        Assert.Equal("ee2", reader.Source.Id);
        Assert.True(reader.Source.CaseSensitiveNames);
        Assert.Null(reader.Natives);

        var set = reader.Read([]);

        Assert.DoesNotContain(set.Diagnostics, d => d.IsError);
        var unit = Assert.Single(set.Records.OfType<UnitRecord>());
        Assert.Equal("Citizen", unit.Id.Name);
        Assert.Null(unit.Legacy);
        Assert.Equal(50.0, unit.MaxHealth.Value.Value);
    }
}
