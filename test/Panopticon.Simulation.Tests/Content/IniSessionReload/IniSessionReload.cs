#nullable enable

using System;
using System.IO;
using System.Text;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Data;
using Panopticon.Data.Ini;
using Panopticon.IO;
using Panopticon.Logic.Object;

namespace Panopticon.Tests.Content.IniSessionReload;

/// <summary>
/// Session reload for the legacy INI path (WI-H6-04). Hot reload means session reload: no file watcher
/// exists in src and none is added. Each call builds a fresh session; nothing is cached between calls.
/// </summary>
internal static class IniSessionReload
{
    /// <summary>
    /// Parses the scratch INI into a new store the way <see cref="ContentManager.LoadIniFile(FileSystemEntry)"/>
    /// does once construction is complete (no unit staging).
    /// </summary>
    public static AssetStore LoadBareSession(ContentRootProbe root)
    {
        var store = new AssetStore(SageGame.CncGenerals, root, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        store.PushScope();
        new IniParser(ReadScratchIni(root), store, SageGame.CncGenerals, new IniDataContext(), Encoding.UTF8).ParseFile();
        return store;
    }

    /// <summary>
    /// Builds a fresh installed session, loads the scratch INI through <see cref="ContentManager.LoadIniFile(FileSystemEntry)"/>,
    /// then loads the staged pack from <paramref name="packDirectory"/>. The pack directory lives outside the content root.
    /// </summary>
    public static Game LoadInstalledSession(GameInstallation installation, ContentRootProbe root, string packDirectory)
    {
        var game = new Game(installation);
        game.ContentManager.LoadIniFile(ReadScratchIni(root));

        var overrides = new[] { new ContentId(ContentSource.Generals, ContentKind.Unit, "*") };
        var pack = game.ContentManager.LoadUnitPack(packDirectory, overrides: overrides);
        if (!pack.Succeeded)
        {
            game.Dispose();
            throw new InvalidOperationException("Staged pack failed to load: " + string.Join("; ", pack.Errors));
        }

        return game;
    }

    private static FileSystemEntry ReadScratchIni(ContentRootProbe root) =>
        root.GetFile(ScratchContentRoot.IniRelativePath)
        ?? throw new FileNotFoundException("Scratch INI is missing.", ScratchContentRoot.IniRelativePath);
}
