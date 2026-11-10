#nullable enable

using System;
using System.IO;
using System.Threading;
using Panopticon.Content;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Logic.Object;
using Panopticon.Tests.Data;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Content.IniSessionReload;

/// <summary>
/// WI-H6-04: an INI object-definition edit followed by a session reload changes the loaded definition,
/// and the run creates or reads no package file under the content root.
///
/// Hot reload here means session reload. No file watcher exists in src, so none is added; each test
/// edits the scratch INI, builds a new session, and asserts the new session reflects the edit.
/// </summary>
public sealed class IniSessionReloadTests
{
    private sealed record DefinitionFacts(string Side, float BuildCost, int WeaponSetCount);

    private sealed record SessionFacts(int ScopeDepth, DefinitionFacts Probe, DefinitionFacts Humvee, DefinitionFacts Crusader);

    [Fact]
    public void BareSession_EditThenSessionReload_ChangesLoadedObjectDefinition()
    {
        using var root = new ScratchContentRoot();
        using var probe = new ContentRootProbe(root.RootDirectory);
        root.WriteObjectDefinition(buildCost: 100);

        var before = IniSessionReload.LoadBareSession(probe);
        Assert.Equal(new DefinitionFacts("America", 100f, 0), Facts(before, ScratchContentRoot.ObjectName));

        root.WriteObjectDefinition(buildCost: 250);

        var after = IniSessionReload.LoadBareSession(probe);
        Assert.Equal(new DefinitionFacts("America", 250f, 0), Facts(after, ScratchContentRoot.ObjectName));
        Assert.Equal(100f, Facts(before, ScratchContentRoot.ObjectName).BuildCost);

        root.AssertNoPackageInteraction(probe);
    }

    [GameFact(SageGame.CncGenerals)]
    public void InstalledSession_EditThenSessionReload_KeepsBuiltInAndPackEffectsConsistent()
    {
        using var root = new ScratchContentRoot();
        using var probe = new ContentRootProbe(root.RootDirectory);
        using var packRoot = new ScratchContentRoot();
        var packDirectory = Path.Combine(packRoot.RootDirectory, "p6b-pack");
        ConvertP6bPack(packDirectory);
        root.WriteObjectDefinition(buildCost: 100);

        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);

        SessionFacts before;
        using (var game = IniSessionReload.LoadInstalledSession(installation, probe, packDirectory))
        {
            before = Snapshot(game.AssetStore);
        }

        root.WriteObjectDefinition(buildCost: 250);

        SessionFacts after;
        using (var game = IniSessionReload.LoadInstalledSession(installation, probe, packDirectory))
        {
            after = Snapshot(game.AssetStore);
        }

        Assert.Equal(100f, before.Probe.BuildCost);
        Assert.Equal(250f, after.Probe.BuildCost);
        Assert.Equal(before.ScopeDepth, after.ScopeDepth);
        Assert.Equal(before.Humvee, after.Humvee);
        Assert.Equal(before.Crusader, after.Crusader);

        root.AssertNoPackageInteraction(probe);
    }

    [Fact]
    public void ContentRootProbe_RejectsManifestAndPackageDirectoryAccess()
    {
        using var root = new ScratchContentRoot();
        using var probe = new ContentRootProbe(root.RootDirectory);
        var packageDirectory = Path.Combine(root.RootDirectory, "pkg-01");
        Directory.CreateDirectory(packageDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, UnitPackageFormat.ManifestFileName), "{}");
        File.WriteAllText(Path.Combine(packageDirectory, "records.bin"), "x");

        Assert.Throws<InvalidOperationException>(() => { probe.GetFile(Path.Combine("pkg-01", UnitPackageFormat.ManifestFileName)); });
        Assert.Throws<InvalidOperationException>(() => { probe.GetFile(Path.Combine("pkg-01", "records.bin")); });
        Assert.Throws<InvalidOperationException>(() => { probe.GetFilesInDirectory("pkg-01"); });
        Assert.Null(probe.GetFile(Path.Combine("missing", "x.ini")));
    }

    private static DefinitionFacts Facts(AssetStore store, string name)
    {
        var definition = store.ObjectDefinitions.GetByName(name);
        Assert.NotNull(definition);
        return new DefinitionFacts(definition.Side, definition.BuildCost, definition.WeaponSets.Count);
    }

    private static SessionFacts Snapshot(AssetStore store) => new(
        store.ScopeDepth,
        Facts(store, ScratchContentRoot.ObjectName),
        Facts(store, "AmericaVehicleHumvee"),
        Facts(store, "AmericaTankCrusader"));

    private static void ConvertP6bPack(string packDirectory)
    {
        using var stdOut = new StringWriter();
        using var errOut = new StringWriter();
        var exitCode = H5MigrateCli.Run(
            ["convert-ini", "--p6b", "--out", packDirectory],
            stdOut,
            errOut,
            CancellationToken.None);
        Assert.True(exitCode == 0, $"convert-ini --p6b failed: {errOut}");
    }
}
