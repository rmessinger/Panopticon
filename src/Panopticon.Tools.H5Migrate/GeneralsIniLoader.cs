#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Text;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Data.Ini;
using Panopticon.IO;

namespace Panopticon.Tools.H5Migrate;

/// <summary>
/// Loads legacy Generals INI definitions into parse-time staging (WI-H5-02).
/// Resolves the internal INI parser and AssetStore seams without requiring engine modifications.
/// </summary>
internal static class GeneralsIniLoader
{
    private static readonly Type IniParserType =
        typeof(SageGame).Assembly.GetType("Panopticon.Data.Ini.IniParser")
        ?? throw new InvalidOperationException("Panopticon.Data.Ini.IniParser type not found.");

    private static readonly MethodInfo ParseFileMethod =
        IniParserType.GetMethod("ParseFile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("IniParser.ParseFile method not found.");

    private static readonly MethodInfo PushScopeMethod =
        typeof(AssetStore).GetMethod("PushScope", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("AssetStore.PushScope method not found.");

    public static UnitContentStaging Load(FileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var staging = new UnitContentStaging();
        var dataContext = new IniDataContext();

        var store = (AssetStore)Activator.CreateInstance(
            typeof(AssetStore),
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            args: [SageGame.CncGenerals, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None],
            culture: null)!;

        PushScopeMethod.Invoke(store, null);

        void LoadFile(string path)
        {
            var entry = fileSystem.GetFile(path);
            if (entry is not null)
            {
                var parser = Activator.CreateInstance(
                    IniParserType,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null,
                    args: [entry, store, SageGame.CncGenerals, dataContext, Encoding.UTF8, staging],
                    culture: null)!;
                ParseFileMethod.Invoke(parser, null);
            }
        }

        LoadFile(@"Data\INI\Default\GameData.ini");
        LoadFile(@"Data\INI\GameData.ini");
        LoadFile(@"Data\INI\Armor.ini");
        LoadFile(@"Data\INI\Locomotor.ini");
        LoadFile(@"Data\INI\Weapon.ini");
        LoadFile(@"Data\INI\Default\Upgrade.ini");
        LoadFile(@"Data\INI\Upgrade.ini");
        LoadFile(@"Data\INI\Default\Object.ini");
        foreach (var file in fileSystem.GetFilesInDirectory(@"Data\INI\Object", "*.ini", SearchOption.AllDirectories))
        {
            LoadFile(file.FilePath);
        }

        return staging;
    }
}
