using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Logic.Object;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Card WI-H2a-14: the Generals reference roster, published through the shared pipeline at game
/// boot, keeps the values authored in the installed INI text and its native module coverage.
/// Requires <c>CNC_GENERALS_PATH</c>; skipped when the install is missing.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneralsUnitParityTests(ITestOutputHelper output)
{
    /// <summary>P6b reference roster: infantry, vehicle, aircraft, builder, support, superweapon.</summary>
    public static readonly string[] Roster =
    [
        "AmericaInfantryRanger",
        "AmericaTankCrusader",
        "AmericaVehicleComanche",
        "AmericaVehicleDozer",
        "AmericaVehicleMedic",
        "AmericaParticleCannonUplink",
    ];

    private static ContentId Unit(string name) => new(ContentSource.Generals, ContentKind.Unit, name);

    [GameFact(SageGame.CncGenerals)]
    public void RosterKeepsAuthoredValuesThroughSharedPipeline()
    {
        using var game = new Game(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals));
        var report = game.ContentManager.UnitImport!;
        var fileSystem = game.ContentManager.FileSystem;
        var objects = new AuthoredIniValues(fileSystem, "Object", AuthoredIniValues.IniFiles(fileSystem, @"Data\INI\Object"));
        var weapons = new AuthoredIniValues(fileSystem, "Weapon", [@"Data\INI\Weapon.ini"]);
        var locomotors = new AuthoredIniValues(fileSystem, "Locomotor", [@"Data\INI\Locomotor.ini"]);

        Assert.True(report.Succeeded, string.Join("\n", report.Errors));

        var compared = 0;
        foreach (var name in Roster)
        {
            var record = report.Set.Records.OfType<UnitRecord>().Single(r => r.Id.Equals(Unit(name)));
            Assert.Contains(record.Id, report.Published!.Ids);

            compared += Compare(name, "MaxHealth", objects.Get(name, "MaxHealth"), record.MaxHealth);
            compared += Compare(name, "BuildCost", objects.Get(name, "BuildCost"), record.BuildCost);
            compared += Compare(name, "VisionRange", objects.Get(name, "VisionRange"), record.VisionRange);

            if (objects.Get(name, "BuildTime") is { } seconds)
            {
                Assert.Equal(LogicFrameSpan.FromSeconds((float)seconds, SageGame.CncGenerals).Value, record.BuildTime.Value.Value);
                compared++;
            }

            foreach (var weaponId in record.Weapons.Resolved)
            {
                var weapon = report.Set.Records.OfType<WeaponRecord>().Single(r => r.Id.Equals(weaponId));
                compared += Compare(weaponId.Name, "AttackRange", weapons.Get(weaponId.Name, "AttackRange"), weapon.AttackRange);
                compared += Compare(weaponId.Name, "PrimaryDamage", weapons.Get(weaponId.Name, "PrimaryDamage"), weapon.PrimaryDamage);
                if (weapons.Get(weaponId.Name, "DelayBetweenShots") is { } delay)
                {
                    Assert.Equal(LogicFrameSpan.FromMilliseconds((float)delay, SageGame.CncGenerals).Value, weapon.ReloadTime.Value.Value);
                    compared++;
                }
            }

            foreach (var locomotorId in record.Locomotors.Resolved)
            {
                var locomotor = report.Set.Records.OfType<LocomotorRecord>().Single(r => r.Id.Equals(locomotorId));
                compared += Compare(locomotorId.Name, "Speed", locomotors.Get(locomotorId.Name, "Speed"), locomotor.Speed, tolerance: 1e-3);
            }
        }

        output.WriteLine($"Compared {compared} authored values across {Roster.Length} roster units.");
        Assert.True(compared >= 30, $"Only {compared} authored values were comparable.");
    }

    [GameFact(SageGame.CncGenerals)]
    public void InheritedDefaultsProductionAndUpgradesKeepNativeSemantics()
    {
        using var game = new Game(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals));
        var report = game.ContentManager.UnitImport!;
        var store = game.AssetStore;
        UnitRecord Record(string name) => report.Set.Records.OfType<UnitRecord>().Single(r => r.Id.Equals(Unit(name)));

        var ranger = Record("AmericaInfantryRanger");
        var nativeRanger = store.ObjectDefinitions.GetByName("AmericaInfantryRanger");
        var nativeModules = nativeRanger.Behaviors.Count + nativeRanger.Draws.Count + nativeRanger.ClientUpdates.Count + nativeRanger.ClientBehaviors.Count;
        Assert.Equal(nativeModules, ranger.ResolvedModules.Count());
        Assert.Contains(ranger.ResolvedModules, m => m.Inherited);

        var crusader = Record("AmericaTankCrusader");
        Assert.Contains(crusader.Prerequisites.Resolved, id => id.Name == "AmericaWarFactory");

        var upgrades = new AuthoredIniValues(game.ContentManager.FileSystem, "Upgrade", [@"Data\INI\Upgrade.ini"]);
        var flashBang = report.Set.Records.OfType<UpgradeRecord>().Single(r => r.Id.Name == "Upgrade_AmericaRangerFlashBangGrenade");
        Assert.Equal(upgrades.Get("Upgrade_AmericaRangerFlashBangGrenade", "BuildCost"), flashBang.BuildCost.Value.Value);

        var nativeCrusaderWeapon = store.ObjectDefinitions.GetByName("AmericaTankCrusader").WeaponSets.Values
            .SelectMany(set => set.Slots)
            .First(slot => slot?.Weapon?.Value is not null)
            .Weapon.Value;
        Assert.Same(store.WeaponTemplates.GetByName(crusader.Weapons.Resolved[0].Name), nativeCrusaderWeapon);
    }

    private int Compare(string block, string field, double? authored, Authored<Quantity> published, double tolerance = 1e-4)
    {
        if (authored is null)
        {
            output.WriteLine($"{block}.{field}: not authored as a plain number; skipped.");
            return 0;
        }

        Assert.True(published.IsPresent, $"{block}.{field} is authored but missing from the published record.");
        Assert.True(System.Math.Abs(authored.Value - published.Value.Value) <= tolerance, $"{block}.{field}: authored {authored} but published {published.Value.Value}.");
        return 1;
    }
}
