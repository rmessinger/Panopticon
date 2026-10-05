using System.Linq;
using System.Text;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Data.Ini;
using Panopticon.Logic.Object;
using Panopticon.Tests.Content;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>Synthetic INI parsed by the real parser, then run through the Generals adapter; no install needed.</summary>
public class GeneralsUnitAdapterTests
{
    private const string Ini =
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

        Object DefaultThingTemplate
          InheritableModule
            Behavior = SquishCollide ModuleTag_Squish
            End
          End
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
            Weapon = SECONDARY None
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

        ChildObject VeteranRanger Ranger
          Body = ActiveBody ModuleTag_Body
            MaxHealth = 240.0
          End
          RemoveModule ModuleTag_Squish
        End

        ObjectReskin RangerDesert Ranger
          BuildCost = 300
        End
        """;

    private static AssetStore Parse(string ini)
    {
        using var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(@"Data\INI\Test.ini", ini);

        var store = new AssetStore(SageGame.CncGenerals, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        store.PushScope();
        new IniParser(fileSystem.GetFile(@"Data\INI\Test.ini")!, store, SageGame.CncGenerals, new IniDataContext(), Encoding.UTF8).ParseFile();
        return store;
    }

    private static ContentId Unit(string name) => new(ContentSource.Generals, ContentKind.Unit, name);

    private static UnitRecord Record(GeneralsUnitImportReport report, string name)
    {
        return report.Set.Records.OfType<UnitRecord>().Single(r => r.Id.Equals(Unit(name)));
    }

    [Fact]
    public void ParsedContentPassesSharedPipelineAndIsAdopted()
    {
        var store = Parse(Ini);
        var report = GeneralsUnitImport.Run(store, SageGame.CncGenerals);

        Assert.True(report.Succeeded, string.Join("\n", report.Diagnostics));
        Assert.Contains(Unit("Ranger"), report.Published!.Ids);
        Assert.Throws<System.InvalidOperationException>(() => new Panopticon.Content.Importing.Units.Materialization.UnitContentPublisher(store).Unload(report.Published));
    }

    [Fact]
    public void TypedValuesDescribeNativeDefinitions()
    {
        var report = GeneralsUnitImport.Run(Parse(Ini), SageGame.CncGenerals);
        var ranger = Record(report, "Ranger");

        Assert.Equal(180, ranger.MaxHealth.Value.Value);
        Assert.Equal(225, ranger.BuildCost.Value.Value);
        Assert.Equal(150, ranger.BuildTime.Value.Value);
        Assert.Equal(QuantityUnit.LogicFrames, ranger.BuildTime.Value.Unit);
        Assert.Equal(["INFANTRY", "SELECTABLE"], ranger.KindOf.Resolved.Order());
        Assert.Equal(["RangerRifle"], ranger.Weapons.Resolved.Select(id => id.Name));
        Assert.Equal("InfantryArmor", ranger.Armor.Value.Name);

        var locomotor = report.Set.Records.OfType<LocomotorRecord>().Single();
        Assert.Equal(30, locomotor.Speed.Value.Value, 3);
        Assert.Equal(180, locomotor.TurnRate.Value.Value, 3);
        Assert.Equal(["GROUND", "RUBBLE"], locomotor.Surfaces.Resolved);

        var weapon = report.Set.Records.OfType<WeaponRecord>().Single();
        // 500 ms becomes 16 frames through the engine's single-precision conversion.
        Assert.Equal(16, weapon.ReloadTime.Value.Value);
        Assert.Equal("SMALL_ARMS", weapon.DamageType.Value);
    }

    [Fact]
    public void ChildAndReskinKeepNativeInheritance()
    {
        var store = Parse(Ini);
        var report = GeneralsUnitImport.Run(store, SageGame.CncGenerals);

        Assert.Equal(240, Record(report, "VeteranRanger").MaxHealth.Value.Value);
        Assert.Equal(300, Record(report, "RangerDesert").BuildCost.Value.Value);
        Assert.Equal(180, Record(report, "RangerDesert").MaxHealth.Value.Value);
        Assert.DoesNotContain(Record(report, "VeteranRanger").ResolvedModules, m => m.Tag == "ModuleTag_Squish");
        Assert.Contains(Record(report, "Ranger").ResolvedModules, m => m.Tag == "ModuleTag_Squish" && m.Inherited && m.InheritanceMode == ModuleInheritanceMode.AlwaysInherit);
    }

    [Fact]
    public void ModulesCarryLegacyBindingsToNativeData()
    {
        var store = Parse(Ini);
        var adapter = new GeneralsUnitAdapter(store, SageGame.CncGenerals);
        var set = adapter.Read([]);
        var ranger = set.Records.OfType<UnitRecord>().Single(r => r.Id.Equals(Unit("Ranger")));
        var native = store.ObjectDefinitions.GetByName("Ranger");

        var body = ranger.ResolvedModules.Single(m => m.Tag == "ModuleTag_Body");
        Assert.Equal("ActiveBody", body.Family);
        Assert.Same(native.Behaviors["ModuleTag_Body"].Data, adapter.Natives.Get(body.Legacy!.Handle));
        Assert.Same(native, adapter.Natives.Get(ranger.Legacy!.Handle));
    }

    [Fact]
    public void LazyReferencesStillResolveToNativeTemplates()
    {
        var store = Parse(Ini);
        GeneralsUnitImport.Run(store, SageGame.CncGenerals).ThrowIfFailed();

        var native = store.ObjectDefinitions.GetByName("Ranger");
        Assert.Same(store.WeaponTemplates.GetByName("RangerRifle"), native.WeaponSets.Values.Single().Slots[0].Weapon.Value);
    }

    [Fact]
    public void DanglingReferenceFailsTheImport()
    {
        var report = GeneralsUnitImport.Run(Parse(Ini.Replace("Weapon = PRIMARY RangerRifle", "Weapon = PRIMARY MissingRifle")), SageGame.CncGenerals);

        Assert.False(report.Succeeded);
        Assert.Contains(report.Errors, d => d.Code == DiagnosticCodes.MissingDependency && d.Message.Contains("MissingRifle"));
        Assert.Throws<System.InvalidOperationException>(report.ThrowIfFailed);
    }

    [Fact]
    public void RegistryCoversEveryNativeModuleFamily()
    {
        var registry = GeneralsUnitAdapter.CreateRegistry();

        Assert.Equal(ModuleSupport.Legacy, registry.GetModuleSupport("SquishCollide"));
        Assert.Equal(ModuleSupport.Typed, registry.GetModuleSupport("ActiveBody"));
        Assert.Equal(ModuleSupport.Unsupported, registry.GetModuleSupport("EpochAdvance"));
    }
}
