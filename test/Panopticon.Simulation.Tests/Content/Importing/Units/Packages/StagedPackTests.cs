#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Data.Ini;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Tests.Content.Tools.H5Migrate;
using Panopticon.Tests.Data;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

public sealed class StagedPackTests : IDisposable
{
    private readonly string _tempRoot;

    public StagedPackTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "staged_pack_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of temp directories
            }
        }
    }

    [Fact]
    public void Synthetic_OverrideRejection_WithoutAllowlist_FailsPublicationAndLeavesScopeDepthUnchanged()
    {
        var packageDir = Path.Combine(_tempRoot, "synthetic-pkg");
        SyntheticUnitPackage.ConvertToPackage(packageDir);

        using var fileSystem = new MemoryFileSystem();
        var store = new AssetStore(SageGame.CncGenerals, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        store.PushScope();

        // Populate base scope with an existing unit definition matching the synthetic package root
        var existingDef = new ObjectDefinition(SyntheticUnitPackage.RootName);
        store.ObjectDefinitions.Add(existingDef);
        var initialDepth = store.ScopeDepth;

        var packDir = Path.Combine(_tempRoot, "pack");
        Directory.CreateDirectory(packDir);
        var packPkgDir = Path.Combine(packDir, "pkg-01");
        CopyDirectory(packageDir, packPkgDir);

        var packReader = new StagedPackReader(packDir);
        // Attempt load without overrides
        var result = packReader.LoadAndPublish(store);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.NameCollision);
        Assert.Equal(initialDepth, store.ScopeDepth);
        Assert.Same(existingDef, store.ObjectDefinitions.GetByName(SyntheticUnitPackage.RootName));
    }

    [Fact]
    public void Synthetic_OverrideAllowlist_SucceedsAndShadows_ThenUnloadsRestoringOriginal()
    {
        var packageDir = Path.Combine(_tempRoot, "synthetic-pkg");
        SyntheticUnitPackage.ConvertToPackage(packageDir);

        using var fileSystem = new MemoryFileSystem();
        var store = new AssetStore(SageGame.CncGenerals, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        store.PushScope();

        var existingDef = new ObjectDefinition(SyntheticUnitPackage.RootName);
        store.ObjectDefinitions.Add(existingDef);
        var initialDepth = store.ScopeDepth;

        var packDir = Path.Combine(_tempRoot, "pack");
        Directory.CreateDirectory(packDir);
        var packPkgDir = Path.Combine(packDir, "pkg-01");
        CopyDirectory(packageDir, packPkgDir);

        var packReader = new StagedPackReader(packDir);
        var declaredOverrides = new[] { new ContentId(ContentSource.Generals, ContentKind.Unit, SyntheticUnitPackage.RootName) };
        var result = packReader.LoadAndPublish(store, overrides: declaredOverrides);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.NotNull(result.Publication);
        Assert.Equal(initialDepth + 1, store.ScopeDepth);

        var shadowedDef = store.ObjectDefinitions.GetByName(SyntheticUnitPackage.RootName);
        Assert.NotNull(shadowedDef);
        Assert.NotSame(existingDef, shadowedDef);

        var publisher = new UnitContentPublisher(store);
        result.Publication.Unload(publisher);

        Assert.True(result.Publication.IsUnloaded);
        Assert.Equal(initialDepth, store.ScopeDepth);
        Assert.Same(existingDef, store.ObjectDefinitions.GetByName(SyntheticUnitPackage.RootName));
    }

    [Fact]
    public void Synthetic_FailureAtomicity_MultiPackagePack_RollsBackAllScopes()
    {
        var packageDir = Path.Combine(_tempRoot, "synthetic-pkg");
        SyntheticUnitPackage.ConvertToPackage(packageDir);

        using var fileSystem = new MemoryFileSystem();
        var store = new AssetStore(SageGame.CncGenerals, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        var initialDepth = store.ScopeDepth;

        var packDir = Path.Combine(_tempRoot, "multi-pack");
        Directory.CreateDirectory(packDir);

        // pkg-01 is valid
        var packPkg1 = Path.Combine(packDir, "pkg-01");
        CopyDirectory(packageDir, packPkg1);

        // pkg-02 is invalid (corrupted manifest JSON)
        var packPkg2 = Path.Combine(packDir, "pkg-02");
        Directory.CreateDirectory(packPkg2);
        File.WriteAllText(Path.Combine(packPkg2, UnitPackageFormat.ManifestFileName), "invalid json");

        var packReader = new StagedPackReader(packDir);
        var result = packReader.LoadAndPublish(store);

        Assert.False(result.Succeeded);
        Assert.Null(result.Publication);
        Assert.Equal(initialDepth, store.ScopeDepth);
        Assert.Null(store.ObjectDefinitions.GetByName(SyntheticUnitPackage.RootName));
    }

    [Fact]
    public void Synthetic_ReverseOrderUnload_PopsScopesInExactReverseOrder()
    {
        var packageDir = Path.Combine(_tempRoot, "synthetic-pkg");
        SyntheticUnitPackage.ConvertToPackage(packageDir);

        using var fileSystem = new MemoryFileSystem();
        var store = new AssetStore(SageGame.CncGenerals, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        var initialDepth = store.ScopeDepth;

        var packDir = Path.Combine(_tempRoot, "two-pkg-pack");
        Directory.CreateDirectory(packDir);

        var packPkg1 = Path.Combine(packDir, "pkg-01");
        CopyDirectory(packageDir, packPkg1);

        // Build a distinct synthetic package for pkg-02
        var builder = new UnitPackageTestBuilder()
            .AddRecord("SecondUnit", ContentKind.Unit, "records/Unit/SecondUnit.json", """{"maxHealth": {"value": 100, "unit": "HitPoints"}}""")
            .AddRoot("SecondUnit");
        builder.Build();
        var packPkg2 = Path.Combine(packDir, "pkg-02");
        Directory.CreateDirectory(packPkg2);
        CopyDirectory(builder.PackageDir, packPkg2);
        builder.Dispose();

        var packReader = new StagedPackReader(packDir);
        var result = packReader.LoadAndPublish(store);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.NotNull(result.Publication);
        Assert.Equal(2, result.Publication.PublishedPackages.Count);
        Assert.Equal(initialDepth + 2, store.ScopeDepth);

        var publisher = new UnitContentPublisher(store);
        result.Publication.Unload(publisher);

        Assert.True(result.Publication.IsUnloaded);
        Assert.Equal(initialDepth, store.ScopeDepth);
    }

    [GameFact(SageGame.CncGenerals)]
    public void RealInstall_TwoPackLayering_LoadsBothInScopeOrder_AndUnloadsInReverseOrder()
    {
        var pack1Dir = Path.Combine(_tempRoot, "real-p6b-pkg");
        using var stdOut1 = new StringWriter();
        using var errOut1 = new StringWriter();
        var exitCode1 = H5MigrateCli.Run(
            ["convert-ini", "--p6b", "--out", pack1Dir],
            stdOut1,
            errOut1,
            CancellationToken.None);
        Assert.True(exitCode1 == 0, $"convert-ini --p6b failed: {errOut1}");

        var pack2Dir = Path.Combine(_tempRoot, "real-humvee-pkg");
        using var stdOut2 = new StringWriter();
        using var errOut2 = new StringWriter();
        var exitCode2 = H5MigrateCli.Run(
            ["convert-ini", "--out", pack2Dir, "AmericaVehicleHumvee"],
            stdOut2,
            errOut2,
            CancellationToken.None);
        Assert.True(exitCode2 == 0, $"convert-ini Humvee failed: {errOut2}");

        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        using var game = new Game(installation);
        var store = game.AssetStore;

        var initialDepth = store.ScopeDepth;
        var nativeCrusader = store.ObjectDefinitions.GetByName("AmericaTankCrusader");
        var nativeHumvee = store.ObjectDefinitions.GetByName("AmericaVehicleHumvee");
        Assert.NotNull(nativeCrusader);
        Assert.NotNull(nativeHumvee);

        // Load Pack 1 (P6b roster with wildcard override)
        var pack1Reader = new StagedPackReader(pack1Dir);
        var overrides1 = new[] { new ContentId(ContentSource.Generals, ContentKind.Unit, "*") };
        var result1 = pack1Reader.LoadAndPublish(store, overrides: overrides1);
        Assert.True(result1.Succeeded, string.Join("; ", result1.Errors.Select(e => e.Message)));
        Assert.Equal(initialDepth + 1, store.ScopeDepth);

        var pack1Crusader = store.ObjectDefinitions.GetByName("AmericaTankCrusader");
        Assert.NotNull(pack1Crusader);
        Assert.NotSame(nativeCrusader, pack1Crusader);
        // Humvee is still native
        Assert.Same(nativeHumvee, store.ObjectDefinitions.GetByName("AmericaVehicleHumvee"));

        // Load Pack 2 (Humvee with wildcard override)
        var pack2Reader = new StagedPackReader(pack2Dir);
        var overrides2 = new[] { new ContentId(ContentSource.Generals, ContentKind.Unit, "*") };
        var result2 = pack2Reader.LoadAndPublish(store, overrides: overrides2);
        Assert.True(result2.Succeeded, string.Join("; ", result2.Errors.Select(e => e.Message)));
        Assert.Equal(initialDepth + 2, store.ScopeDepth);

        var pack2Humvee = store.ObjectDefinitions.GetByName("AmericaVehicleHumvee");
        Assert.NotNull(pack2Humvee);
        Assert.NotSame(nativeHumvee, pack2Humvee);
        // Crusader is still from pack 1
        Assert.Same(pack1Crusader, store.ObjectDefinitions.GetByName("AmericaTankCrusader"));

        // Unload Pack 2
        var publisher = new UnitContentPublisher(store);
        result2.Publication!.Unload(publisher);
        Assert.Equal(initialDepth + 1, store.ScopeDepth);
        Assert.Same(nativeHumvee, store.ObjectDefinitions.GetByName("AmericaVehicleHumvee"));
        Assert.Same(pack1Crusader, store.ObjectDefinitions.GetByName("AmericaTankCrusader"));

        // Unload Pack 1
        result1.Publication!.Unload(publisher);
        Assert.Equal(initialDepth, store.ScopeDepth);
        Assert.Same(nativeCrusader, store.ObjectDefinitions.GetByName("AmericaTankCrusader"));
    }

    [GameFact(SageGame.CncGenerals)]
    public void RealInstall_StagedPack_ContentManagerHook_LoadsAndUnloads()
    {
        var packDir = Path.Combine(_tempRoot, "cm-p6b-pkg");
        using var stdOut = new StringWriter();
        using var errOut = new StringWriter();
        var exitCode = H5MigrateCli.Run(
            ["convert-ini", "--p6b", "--out", packDir],
            stdOut,
            errOut,
            CancellationToken.None);
        Assert.True(exitCode == 0, $"convert-ini failed: {errOut}");

        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        using var game = new Game(installation);
        var cm = game.ContentManager;
        var initialDepth = game.AssetStore.ScopeDepth;

        var overrides = new[] { new ContentId(ContentSource.Generals, ContentKind.Unit, "*") };
        var loadResult = cm.LoadUnitPack(packDir, overrides: overrides);
        Assert.True(loadResult.Succeeded, string.Join("; ", loadResult.Errors.Select(e => e.Message)));
        Assert.NotNull(loadResult.Publication);
        Assert.Equal(initialDepth + 1, game.AssetStore.ScopeDepth);

        cm.UnloadUnitPack(loadResult.Publication);
        Assert.Equal(initialDepth, game.AssetStore.ScopeDepth);
    }

    [GameFact(SageGame.CncGenerals)]
    public void RealInstall_OverrideRejection_WithoutAllowlist_FailsAndLeavesScopeDepthUnchanged()
    {
        var packDir = Path.Combine(_tempRoot, "reject-p6b-pkg");
        using var stdOut = new StringWriter();
        using var errOut = new StringWriter();
        var exitCode = H5MigrateCli.Run(
            ["convert-ini", "--p6b", "--out", packDir],
            stdOut,
            errOut,
            CancellationToken.None);
        Assert.True(exitCode == 0, $"convert-ini failed: {errOut}");

        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        using var game = new Game(installation);
        var initialDepth = game.AssetStore.ScopeDepth;

        // Loading without overrides when assets collide with built-ins fails
        var reader = new StagedPackReader(packDir);
        var loadResult = reader.LoadAndPublish(game.AssetStore);

        Assert.False(loadResult.Succeeded);
        Assert.Contains(loadResult.Diagnostics, d => d.Code == DiagnosticCodes.NameCollision);
        Assert.Equal(initialDepth, game.AssetStore.ScopeDepth);
    }

    [GameFact(SageGame.CncGenerals)]
    public void RealInstall_PublishedPackageUnits_SpawnAndRebindThroughRetainedReferenceRebinder()
    {
        var packDir = Path.Combine(_tempRoot, "spawn-p6b-pkg");
        using var stdOut = new StringWriter();
        using var errOut = new StringWriter();
        var exitCode = H5MigrateCli.Run(
            ["convert-ini", "--p6b", "--out", packDir],
            stdOut,
            errOut,
            CancellationToken.None);
        Assert.True(exitCode == 0, $"convert-ini failed: {errOut}");

        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        using var game = new Game(installation, null, new Configuration { NoAudio = true, SkipStartingBuildings = true }, null);
        game.StartSinglePlayerGame("maps/alpine assault/alpine assault.map");

        var overrides = new[] { new ContentId(ContentSource.Generals, ContentKind.Unit, "*") };
        var loadResult = game.ContentManager.LoadUnitPack(packDir, overrides: overrides);
        Assert.True(loadResult.Succeeded, string.Join("; ", loadResult.Errors.Select(e => e.Message)));

        // Verify the published Crusader resolves and spawns without error
        var crusaderDef = game.AssetStore.ObjectDefinitions.GetByName("AmericaTankCrusader");
        Assert.NotNull(crusaderDef);
        Assert.True(crusaderDef.WeaponSets.Count > 0);

        var player = game.PlayerManager.GetCivilianPlayer();
        var spawned = game.Scene3D.GameObjects.CreateObject(crusaderDef, player);
        Assert.NotNull(spawned);
        Assert.NotNull(spawned.BodyModule);
        Assert.True(spawned.BodyModule.MaxHealth > 0);
    }

    [Fact]
    public void PackWithMalformedQuantityValue_FailsPublicationAndLeavesScopeDepthUnchanged()
    {
        var packageDir = Path.Combine(_tempRoot, "synthetic-pkg");
        SyntheticUnitPackage.ConvertToPackage(packageDir);

        UnitPackageTamper.EditRecordPayload(packageDir, SyntheticUnitPackage.RootName, payload =>
        {
            payload["maxHealth"] = new System.Text.Json.Nodes.JsonObject
            {
                ["value"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["value"] = 1,
                    ["unit"] = "1",
                },
                ["unit"] = "HitPoints",
            };
        });

        using var fileSystem = new MemoryFileSystem();
        var store = new AssetStore(SageGame.CncGenerals, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        store.PushScope();
        var initialDepth = store.ScopeDepth;

        var packDir = Path.Combine(_tempRoot, "pack");
        Directory.CreateDirectory(packDir);
        var packPkgDir = Path.Combine(packDir, "pkg-01");
        CopyDirectory(packageDir, packPkgDir);

        var packReader = new StagedPackReader(packDir);
        var result = packReader.LoadAndPublish(store);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Message.Contains("Quantity 'value' must be a finite number or numeric string, got Object."));
        Assert.Equal(initialDepth, store.ScopeDepth);
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: true);
        }
    }
}
