#nullable enable

using System.IO;
using System.Linq;
using System.Text;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Data.Ini;
using Panopticon.IO;
using Panopticon.Tests.Content.Importing.Units.Packages;
using Xunit;

namespace Panopticon.Tests.Content.Tools.H5Migrate;

/// <summary>Converts a synthetic Generals INI roster into an H4 unit package for units-mode round-trip fixtures.</summary>
internal static class SyntheticUnitPackage
{
    public const string RootName = "Ranger";

    public const string WeaponName = "RangerRifle";

    private const string IniPath = @"Data\INI\Object\Synthetic.ini";

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
        """;

    public static Converted ConvertToPackage(string packageDir)
    {
        using var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(IniPath, Ini);

        var store = new AssetStore(SageGame.CncGenerals, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        store.PushScope();
        var staging = new UnitContentStaging();
        new IniParser(fileSystem.GetFile(IniPath)!, store, SageGame.CncGenerals, new IniDataContext(), Encoding.UTF8, staging).ParseFile();

        var adapter = new GeneralsUnitAdapter(staging, SageGame.CncGenerals);
        var set = adapter.Read([RootName]);
        var resolution = UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry());
        Assert.True(resolution.Succeeded, string.Join("; ", resolution.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        var provider = new UnitPackageRetainedIniSlicer(fileSystem);
        var write = H4UnitPackageWriter.Write(packageDir, resolution, provider);
        Assert.True(write.Success, string.Join("; ", write.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        return new Converted(packageDir, resolution, adapter.Natives, provider);
    }

    public sealed record Converted(
        string PackageDir,
        UnitImportResolution Resolution,
        ILegacyNativeTable? Natives,
        IUnitPackageRetainedPayloadProvider Provider);
}
