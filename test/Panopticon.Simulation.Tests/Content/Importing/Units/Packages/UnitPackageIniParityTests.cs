#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Data.Ini;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

/// <summary>
/// Card WI-H4-05: bounded INI parity proof.
/// Loads the bounded Generals reference roster (P6b archetype units, USA reference) both from
/// installed INI and from an H4 package written from that INI.
/// Asserts materialized template equality field by field (ObjectDefinition, WeaponTemplate,
/// ArmorTemplate, LocomotorTemplate, UpgradeTemplate) and validates one bounded behavioral
/// assertion (produce, move, attack) on both paths.
///
/// Claim discipline: parity is proven by this bounded roster run only; no full-roster or EE2
/// claim is made. Physics metadata parity covers the string stable-id fields and absent defaults
/// only (gated on AQ-H4-1).
/// Requires CNC_GENERALS_PATH; skipped vacuously with an explicit reason when the install is absent.
/// </summary>
[Collection("SerialGameTests")]
public sealed class UnitPackageIniParityTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _scratchDir;

    public UnitPackageIniParityTests(ITestOutputHelper output)
    {
        _output = output;
        _scratchDir = Path.Combine(Path.GetTempPath(), "unit_pkg_parity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_scratchDir))
            {
                Directory.Delete(_scratchDir, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void BoundedRoster_MaterializedTemplates_CompareEqualFieldByField()
    {
        var (fileSystem, iniResolution, iniAdapter) = LoadGeneralsIniPipeline();
        var packageRoot = Path.Combine(_scratchDir, "parity-pkg");
        var slicer = new UnitPackageRetainedIniSlicer(fileSystem);

        var writeResult = H4UnitPackageWriter.Write(packageRoot, iniResolution, slicer);
        Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var reader = new H4UnitPackageReader(packageRoot);
        var pkgSet = reader.Read(UnitPackageRoster.ImportRoots);
        Assert.DoesNotContain(pkgSet.Diagnostics, d => d.IsError);

        var pkgResolution = UnitImportResolution.Resolve(pkgSet, GeneralsUnitAdapter.CreateRegistry());
        Assert.True(pkgResolution.Succeeded, string.Join("; ", pkgResolution.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        // Closure record round-trip assertion before materialization
        UnitPackageRoundTripAssert.ClosuresRoundTrip(iniResolution, pkgSet, reader.Natives);

        // Path 1 (INI): materialize and publish into dedicated scoped AssetStore
        var iniStore = CreateScopedStore(fileSystem);
        var iniPublisher = new UnitContentPublisher(iniStore);
        var iniContext = new UnitMaterializationContext(new UnitConversionPolicy(SageGame.CncGenerals), iniPublisher.Lookup, iniAdapter.Natives, iniStore);
        var iniMaterialized = UnitRuntimeMaterializer.Materialize(iniResolution, iniContext);
        Assert.True(iniMaterialized.Succeeded, string.Join("; ", iniMaterialized.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var iniPublication = iniPublisher.Publish(iniMaterialized);
        Assert.True(iniPublication.Succeeded, string.Join("; ", iniPublication.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        // Path 2 (Package): materialize and publish into separate scoped AssetStore
        var pkgStore = CreateScopedStore(fileSystem);
        var pkgPublisher = new UnitContentPublisher(pkgStore);
        var pkgContext = new UnitMaterializationContext(new UnitConversionPolicy(SageGame.CncGenerals), pkgPublisher.Lookup, reader.Natives, pkgStore);
        var pkgMaterialized = UnitRuntimeMaterializer.Materialize(pkgResolution, pkgContext);
        Assert.True(pkgMaterialized.Succeeded, string.Join("; ", pkgMaterialized.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var pkgPublication = pkgPublisher.Publish(pkgMaterialized);
        Assert.True(pkgPublication.Succeeded, string.Join("; ", pkgPublication.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        // Field-by-field equality comparison across all roster units and referenced templates
        var comparisons = UnitPackageTemplateComparer.CompareRosterTemplates(
            iniStore,
            pkgStore,
            UnitPackageRoster.ArchetypeUnits);

        _output.WriteLine($"Field-by-field template equality verified across {UnitPackageRoster.ArchetypeUnits.Count} roster units: {comparisons} assertions passed.");
        Assert.True(comparisons >= 200, $"Expected at least 200 field comparisons, but ran {comparisons}.");
    }

    [GameFact(SageGame.CncGenerals)]
    public void BoundedRoster_BehavioralAssertion_PassesOnBothPaths()
    {
        var (fileSystem, iniResolution, iniAdapter) = LoadGeneralsIniPipeline();
        var packageRoot = Path.Combine(_scratchDir, "behavioral-pkg");
        var slicer = new UnitPackageRetainedIniSlicer(fileSystem);

        var writeResult = H4UnitPackageWriter.Write(packageRoot, iniResolution, slicer);
        Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var reader = new H4UnitPackageReader(packageRoot);
        var pkgSet = reader.Read(UnitPackageRoster.ImportRoots);
        Assert.DoesNotContain(pkgSet.Diagnostics, d => d.IsError);

        var pkgResolution = UnitImportResolution.Resolve(pkgSet, GeneralsUnitAdapter.CreateRegistry());
        Assert.True(pkgResolution.Succeeded, string.Join("; ", pkgResolution.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        // Path 1 (INI): materialize and publish
        var iniStore = CreateScopedStore(fileSystem);
        var iniPublisher = new UnitContentPublisher(iniStore);
        var iniContext = new UnitMaterializationContext(new UnitConversionPolicy(SageGame.CncGenerals), iniPublisher.Lookup, iniAdapter.Natives, iniStore);
        var iniMaterialized = UnitRuntimeMaterializer.Materialize(iniResolution, iniContext);
        Assert.True(iniMaterialized.Succeeded);
        var iniPublication = iniPublisher.Publish(iniMaterialized);
        Assert.True(iniPublication.Succeeded);

        // Path 2 (Package): materialize and publish
        var pkgStore = CreateScopedStore(fileSystem);
        var pkgPublisher = new UnitContentPublisher(pkgStore);
        var pkgContext = new UnitMaterializationContext(new UnitConversionPolicy(SageGame.CncGenerals), pkgPublisher.Lookup, reader.Natives, pkgStore);
        var pkgMaterialized = UnitRuntimeMaterializer.Materialize(pkgResolution, pkgContext);
        Assert.True(pkgMaterialized.Succeeded);
        var pkgPublication = pkgPublisher.Publish(pkgMaterialized);
        Assert.True(pkgPublication.Succeeded);

        // Bounded produce, move, attack check on Path 1 (INI)
        var iniBehaviorResult = RunProduceMoveAttack(iniStore);
        _output.WriteLine($"Path 1 (INI) behavioral result: produce={iniBehaviorResult.Produced}, move={iniBehaviorResult.Moved}, attack={iniBehaviorResult.Attacked}");
        Assert.True(iniBehaviorResult.AllPassed, iniBehaviorResult.Detail);

        // Bounded produce, move, attack check on Path 2 (Package)
        var pkgBehaviorResult = RunProduceMoveAttack(pkgStore);
        _output.WriteLine($"Path 2 (Package) behavioral result: produce={pkgBehaviorResult.Produced}, move={pkgBehaviorResult.Moved}, attack={pkgBehaviorResult.Attacked}");
        Assert.True(pkgBehaviorResult.AllPassed, pkgBehaviorResult.Detail);
    }

    private static (FileSystem FileSystem, UnitImportResolution Resolution, GeneralsUnitAdapter Adapter) LoadGeneralsIniPipeline()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        var fileSystem = installation.CreateFileSystem();

        var staging = new UnitContentStaging();
        var dataContext = new IniDataContext();
        var parseStore = new AssetStore(SageGame.CncGenerals, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        parseStore.PushScope();

        void LoadIni(string path)
        {
            var entry = fileSystem.GetFile(path);
            if (entry is not null)
            {
                var parser = new IniParser(entry, parseStore, SageGame.CncGenerals, dataContext, Encoding.UTF8, staging);
                parser.ParseFile();
            }
        }

        LoadIni(@"Data\INI\Default\GameData.ini");
        LoadIni(@"Data\INI\GameData.ini");
        LoadIni(@"Data\INI\Armor.ini");
        LoadIni(@"Data\INI\Locomotor.ini");
        LoadIni(@"Data\INI\Weapon.ini");
        LoadIni(@"Data\INI\Default\Upgrade.ini");
        LoadIni(@"Data\INI\Upgrade.ini");
        LoadIni(@"Data\INI\Default\Object.ini");
        foreach (var file in fileSystem.GetFilesInDirectory(@"Data\INI\Object", "*.ini", SearchOption.AllDirectories))
        {
            LoadIni(file.FilePath);
        }

        var adapter = new GeneralsUnitAdapter(staging, SageGame.CncGenerals);
        var set = adapter.Read(UnitPackageRoster.ImportRoots);
        var resolution = UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry());
        Assert.True(resolution.Succeeded, string.Join("; ", resolution.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        return (fileSystem, resolution, adapter);
    }

    private static AssetStore CreateScopedStore(FileSystem fileSystem)
    {
        var store = new AssetStore(SageGame.CncGenerals, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        store.PushScope();
        return store;
    }

    private static (bool Produced, bool Moved, bool Attacked, bool AllPassed, string Detail) RunProduceMoveAttack(AssetStore store)
    {
        var game = new MockedGameTest.TestGame(SageGame.CncGenerals);

        var human = game.PlayerManager.GetPlayerByIndex(1);
        var ai = game.PlayerManager.GetPlayerByIndex(0);

        // 1. Produce: instantiate reference unit definition
        var unitDef = store.ObjectDefinitions.GetByName("AmericaTankCrusader")
            ?? throw new InvalidOperationException("AmericaTankCrusader definition not found in store.");
        var unit = game.GameLogic.CreateObject(unitDef, human);
        if (unit is null || unit.Definition.Name != "AmericaTankCrusader" || unit.BodyModule.Health <= 0 || unit.AIUpdate is null)
        {
            return (false, false, false, false, "Produce failed: unit creation or initialization was invalid.");
        }

        // 2. Move: dispatch MoveTo order and verify AIUpdate target point
        human.SelectUnits([unit]);
        var destination = new Vector3(120f, 240f, 0f);
        var moveOrder = new Order(1, OrderType.MoveTo);
        moveOrder.AddPositionArgument(destination);
        new OrderProcessor(game).Process([moveOrder]);
        var moved = unit.AIUpdate.TargetPoints.Contains(destination);
        if (!moved)
        {
            return (true, false, false, false, "Move failed: unit AIUpdate target point did not match destination.");
        }

        // 3. Attack: dispatch AttackObject order and verify engage target point
        var targetDef = store.ObjectDefinitions.GetByName("AmericaPowerPlant")
            ?? throw new InvalidOperationException("AmericaPowerPlant definition not found in store.");
        var target = game.GameLogic.CreateObject(targetDef, ai);
        if (target is null)
        {
            return (true, true, false, false, "Attack target creation failed.");
        }

        target.UpdateTransform(new Vector3(450f, 550f, 0f), null);
        var attackOrder = new Order(1, OrderType.AttackObject);
        attackOrder.AddObjectIdArgument(target.Id);
        new OrderProcessor(game).Process([attackOrder]);
        var attacked = unit.AIUpdate.TargetPoints.Contains(target.Translation);
        if (!attacked)
        {
            return (true, true, false, false, "Attack failed: unit AIUpdate target point did not match target translation.");
        }

        return (true, true, true, true, "Produce, move, and attack assertions all passed.");
    }
}
