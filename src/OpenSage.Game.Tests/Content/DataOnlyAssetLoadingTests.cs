using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenSage.Content;
using OpenSage.Data.Ini;
using OpenSage.IO;
using OpenSage.Logic.Object;
using Xunit;

namespace OpenSage.Tests.Content;

public sealed class MemoryFileSystem : FileSystem
{
    private readonly Dictionary<string, byte[]> _files = new();

    public void AddFile(string path, string contents) =>
        _files[NormalizeFilePath(path)] = Encoding.UTF8.GetBytes(contents);

    public override FileSystemEntry? GetFile(string filePath) =>
        _files.TryGetValue(NormalizeFilePath(filePath), out var bytes)
            ? new FileSystemEntry(this, filePath, (uint)bytes.Length, () => new MemoryStream(bytes))
            : null;

    public override IEnumerable<FileSystemEntry> GetFilesInDirectory(
        string directoryPath,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        var normalizedDirectory = NormalizeFilePath(directoryPath);
        foreach (var (path, bytes) in _files)
        {
            if (path.StartsWith(normalizedDirectory))
            {
                yield return new FileSystemEntry(this, path, (uint)bytes.Length, () => new MemoryStream(bytes));
            }
        }
    }
}

/// <summary>
/// A1-2 data-only asset loading: an AssetStore built with null graphics parameters and the
/// None load strategy resolves logic-relevant INI data with no GraphicsDevice, while GPU-backed
/// asset kinds return null (WI-A1-04).
/// </summary>
public class DataOnlyAssetLoadingTests
{
    private const string IniContents = """
        Locomotor TestDataOnlyLocomotor
          Surfaces = GROUND
          Speed = 30
          TurnRate = 60
        End

        Weapon TestDataOnlyWeapon
          PrimaryDamage = 10
          PrimaryDamageRadius = 5.0
        End

        Object TestDataOnlyObject
          Side = America
        End
        """;

    private static AssetStore CreateDataOnlyAssetStore(FileSystem fileSystem)
    {
        var assetStore = new AssetStore(
            SageGame.CncGenerals,
            fileSystem,
            language: null,
            graphicsDevice: null,
            standardGraphicsResources: null,
            shaderResources: null,
            shaderSetStore: null,
            OnDemandAssetLoadStrategy.None);
        assetStore.PushScope();
        return assetStore;
    }

    [Fact]
    public void DataOnlyAssetStore_ResolvesLogicRelevantDefinitions()
    {
        using var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(@"Data\INI\TestData.ini", IniContents);

        var assetStore = CreateDataOnlyAssetStore(fileSystem);

        var parser = new IniParser(
            fileSystem.GetFile(@"Data\INI\TestData.ini")!,
            assetStore,
            SageGame.CncGenerals,
            new IniDataContext(),
            Encoding.UTF8);
        parser.ParseFile();

        Assert.NotNull(assetStore.ObjectDefinitions.GetByName("TestDataOnlyObject"));
        Assert.NotNull(assetStore.WeaponTemplates.GetByName("TestDataOnlyWeapon"));
        Assert.NotNull(assetStore.LocomotorTemplates.GetByName("TestDataOnlyLocomotor"));

        // GPU-backed kinds stay unloadable under the None strategy; the existing behavior
        // is a null return, not a throw.
        Assert.Null(assetStore.Textures.GetByName("TestDataOnlyObject"));
        Assert.Null(assetStore.Models.GetByName("TestDataOnlyObject"));
    }
}
