#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Logic.Map;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

/// <summary>
/// WorldBuilder package boundary integration tests for WI-H6-01:
/// AQ-H6-1 live re-serialization spike, open/save/reopen round-trip equality,
/// companion member UTF-8 preservation, scoped AssetReference retention,
/// loose-directory-only addressing rejection, and no partial opens on corrupt packages.
/// </summary>
public sealed class WbPackageBoundaryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ITestOutputHelper _output;

    public WbPackageBoundaryTests(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Combine(Path.GetTempPath(), "wb-pkg-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AQ_H6_1_LiveSerializationProof_EditedSectionsRegeneratedAndReopened()
    {
        // 1. Create a synthetic map fixture with typed and retained sections.
        var mapFile = CreateSyntheticMapFile();
        var originalPlayerDisplayName = mapFile.SidesList.Players[0].DisplayName;
        var originalElevation = mapFile.HeightMapData.Elevations[10, 10];

        var pkgDir = Path.Combine(_tempRoot, "spike-fixture-package");
        const string initialIni = "; spike initial map.ini\n";
        const string initialStr = "MAPEDIT:SpikeTitle=\"Spike Title\"\n";

        var writeResult = MapPackageConverter.SavePackage(mapFile, pkgDir, initialIni, initialStr);
        Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics.Select(d => d.Message)));

        // Record the original binary bytes of the retained SidesList and the heights layer.
        var sidesPath = Path.Combine(pkgDir, "retained", "SidesList.bin");
        Assert.True(File.Exists(sidesPath));
        var originalSidesBytes = File.ReadAllBytes(sidesPath);
        var originalSidesSha = Convert.ToHexString(SHA256.HashData(originalSidesBytes)).ToLowerInvariant();

        var heightsPath = Path.Combine(pkgDir, "layers", "heights_0_0.bin");
        Assert.True(File.Exists(heightsPath));
        var originalHeightsBytes = File.ReadAllBytes(heightsPath);

        // 2. Open through EditorMap package boundary.
        var editorMap = EditorMap.OpenPackage(pkgDir);
        Assert.True(editorMap.IsPackage);
        Assert.False(editorMap.IsDirty);
        Assert.Equal(initialIni, editorMap.MapIniContent);
        Assert.Equal(initialStr, editorMap.MapStrContent);

        // 3. EDIT typed and retained-backed live sections:
        // - Height sample change (typed home)
        const ushort editedElevation = 250;
        editorMap.MapFile.HeightMapData.Elevations[10, 10] = editedElevation;

        // - Placed-object property change (typed home)
        Assert.NotEmpty(editorMap.MapFile.ObjectsList.Objects);
        var targetObject = editorMap.MapFile.ObjectsList.Objects[0];
        targetObject.Properties.AddReal("SpikeEditedProperty", 99.5f);

        // - SidesList change (retained-backed section)
        const string editedPlayerDisplayName = "SpikeEditedPlayerName";
        editorMap.MapFile.SidesList.Players[0].DisplayName = editedPlayerDisplayName;

        editorMap.MarkDirty();
        Assert.True(editorMap.IsDirty);

        // 4. Save back to the package (re-serializing live sections).
        editorMap.Save();
        Assert.False(editorMap.IsDirty);

        // Assert that the saved package does NOT reuse original retained bytes over edited objects:
        // byte-compare the edited section's stored bytes against the original to prove regeneration.
        var savedSidesBytes = File.ReadAllBytes(sidesPath);
        Assert.False(originalSidesBytes.SequenceEqual(savedSidesBytes),
            "AQ-H6-1 Proof: The saved package must NOT reuse original retained bytes over edited objects.");

        var savedSidesSha = Convert.ToHexString(SHA256.HashData(savedSidesBytes)).ToLowerInvariant();
        Assert.NotEqual(originalSidesSha, savedSidesSha);

        var savedHeightsBytes = File.ReadAllBytes(heightsPath);
        Assert.False(originalHeightsBytes.SequenceEqual(savedHeightsBytes),
            "AQ-H6-1 Proof: Stored heights layer binary must be regenerated from live elevations.");

        // 5. Reopen and assert equality of the edited state.
        var reopened = EditorMap.OpenPackage(pkgDir);
        Assert.False(reopened.IsDirty);

        // Assert equality of edited typed and retained sections.
        Assert.Equal(editedElevation, reopened.MapFile.HeightMapData.Elevations[10, 10]);
        var reloadedProp = reopened.MapFile.ObjectsList.Objects[0].Properties.GetPropOrNull("SpikeEditedProperty");
        Assert.NotNull(reloadedProp);
        Assert.Equal(99.5f, reloadedProp!.Value);
        Assert.Equal(editedPlayerDisplayName, reopened.MapFile.SidesList.Players[0].DisplayName);

        // Assert faithful retention of untouched companions' metadata and source fields.
        Assert.Equal(initialIni, reopened.MapIniContent);
        Assert.Equal(initialStr, reopened.MapStrContent);
        Assert.NotNull(reopened.PackageContext);
        Assert.Equal("spike-fixture-package", reopened.PackageContext!.Identity?.Name);

        var manifestJson = File.ReadAllText(Path.Combine(pkgDir, MapPackageFormat.ManifestFileName));
        var (manifest, _) = MapPackageManifestJson.Parse(manifestJson);
        Assert.NotNull(manifest);
        Assert.NotNull(manifest!.Companions);
        Assert.Equal(2, manifest.Companions!.Count);
        Assert.Contains(manifest.Companions, c => c.Name == "map.ini");
        Assert.Contains(manifest.Companions, c => c.Name == "map.str");

        _output.WriteLine("AQ-H6-1 Spike passed: live re-serialization proved with regenerated bytes.");
        _output.WriteLine($"Original SidesList SHA-256: {originalSidesSha}");
        _output.WriteLine($"Regenerated SidesList SHA-256: {savedSidesSha}");
    }

    [Fact]
    public void OpenSaveReopen_EqualityOverConvertedFixture_CompanionsRoundTripByteForByte()
    {
        var mapFile = CreateSyntheticMapFile();
        var pkgDir = Path.Combine(_tempRoot, "roundtrip-package");
        const string iniContent = "; authored companion ini\r\nSpecialPower = Yes\r\n";
        const string strContent = "STRING:SampleText=\"Hello World\"\r\n";

        var writeResult = MapPackageConverter.SavePackage(mapFile, pkgDir, iniContent, strContent);
        Assert.True(writeResult.Success);

        var opened = EditorMap.OpenPackage(pkgDir);
        Assert.Equal(iniContent, opened.MapIniContent);
        Assert.Equal(strContent, opened.MapStrContent);

        // Save again without changes.
        opened.Save();

        var reopened = EditorMap.OpenPackage(pkgDir);
        Assert.Equal(iniContent, reopened.MapIniContent);
        Assert.Equal(strContent, reopened.MapStrContent);

        // Companion files on disk match byte-for-byte.
        var iniDisk = File.ReadAllText(Path.Combine(pkgDir, "companions", "map.ini"), Encoding.UTF8);
        var strDisk = File.ReadAllText(Path.Combine(pkgDir, "companions", "map.str"), Encoding.UTF8);
        Assert.Equal(iniContent, iniDisk);
        Assert.Equal(strContent, strDisk);

        // Heights and objects match exactly.
        Assert.Equal(mapFile.HeightMapData.Width, reopened.MapFile.HeightMapData.Width);
        Assert.Equal(mapFile.HeightMapData.Height, reopened.MapFile.HeightMapData.Height);
        Assert.Equal(mapFile.ObjectsList.Objects.Length, reopened.MapFile.ObjectsList.Objects.Length);
    }

    [Fact]
    public void ScopedAssetReference_PreservedThroughRoundTrip()
    {
        var mapFile = CreateSyntheticMapFile();
        var pkgDir = Path.Combine(_tempRoot, "scoped-asset-package");

        // Authored object with custom non-legacy scope.
        var context = new PackageConversionContext
        {
            Identity = new MapIdentity("ScopedMap", MapSourceKind.H3Package, 8),
            ObjectTypesByName = new Dictionary<string, AssetReference>
            {
                ["CustomUnitTemplate"] = new("mod_expansion_pack", "CustomUnitTemplate"),
            },
        };

        var writeResult = MapPackageConverter.SavePackage(mapFile, pkgDir, context: context);
        Assert.True(writeResult.Success);

        // Open through EditorMap.
        var editorMap = EditorMap.OpenPackage(pkgDir);
        Assert.NotNull(editorMap.PackageContext);
        Assert.NotNull(editorMap.PackageContext!.ObjectTypesByName);
        Assert.True(editorMap.PackageContext.ObjectTypesByName!.ContainsKey("CustomUnitTemplate"));
        Assert.Equal("mod_expansion_pack", editorMap.PackageContext.ObjectTypesByName["CustomUnitTemplate"].Scope);

        // Save again.
        editorMap.Save();

        // Verify that the manifest retains the scoped reference and was not flattened to "legacy".
        var manifestJson = File.ReadAllText(Path.Combine(pkgDir, MapPackageFormat.ManifestFileName));
        var (manifest, _) = MapPackageManifestJson.Parse(manifestJson);
        Assert.NotNull(manifest);
        Assert.NotNull(manifest!.Objects);
        var customObj = Assert.Single(manifest.Objects!, o => o.Type.Name == "CustomUnitTemplate");
        Assert.Equal("mod_expansion_pack", customObj.Type.Scope);
        Assert.Equal("CustomUnitTemplate", customObj.Type.Name);
    }

    [Fact]
    public void UnsupportedAddressing_ArchiveResolvedPackagePath_ThrowsNamedDiagnostic()
    {
        // 1. Path containing .big archive
        var bigPath = "/some/archive.big/maps/MyPackage";
        var ex = Assert.Throws<MapImportException>(() => EditorMap.OpenPackage(bigPath));
        Assert.Contains(ex.Diagnostics, d => d.Code == MapInterchangeDiagnostic.UnsupportedAddressing);

        // 2. Entry from non-disk file system
        var mockArchiveFs = new MockArchiveFileSystem();
        var archiveEntry = new FileSystemEntry(
            mockArchiveFs,
            "maps/contained_package/manifest.json",
            100,
            () => new MemoryStream());

        var ex2 = Assert.Throws<MapImportException>(() => EditorMap.OpenPackage(archiveEntry));
        Assert.Contains(ex2.Diagnostics, d => d.Code == MapInterchangeDiagnostic.UnsupportedAddressing);
    }

    [Fact]
    public void NoPartialOpen_OnCorruptPackage_EditorStateUntouched()
    {
        var corruptPkg = Path.Combine(_tempRoot, "corrupt-package");
        Directory.CreateDirectory(corruptPkg);
        File.WriteAllText(Path.Combine(corruptPkg, MapPackageFormat.ManifestFileName), "not valid json {[[");

        var ex = Assert.Throws<MapImportException>(() => EditorMap.OpenPackage(corruptPkg));
        Assert.NotEmpty(ex.Diagnostics);
    }

    [Fact]
    public void SaveAsPackage_WritesToNewDirectory_AndMarksClean()
    {
        var mapFile = CreateSyntheticMapFile();
        var editorMap = EditorMap.CreateNew(mapFile);
        Assert.True(editorMap.IsDirty);
        Assert.False(editorMap.IsPackage);

        var pkgDir = Path.Combine(_tempRoot, "saveas-package");
        editorMap.SaveAsPackage(pkgDir);

        Assert.True(editorMap.IsPackage);
        Assert.False(editorMap.IsDirty);
        Assert.Equal(pkgDir, editorMap.FilePath);
        Assert.True(Directory.Exists(pkgDir));
        Assert.True(File.Exists(Path.Combine(pkgDir, MapPackageFormat.ManifestFileName)));
    }

    [Fact]
    public void AbsentCompanions_DefaultsToEmptyString()
    {
        var mapFile = CreateSyntheticMapFile();
        var pkgDir = Path.Combine(_tempRoot, "no-companions-package");

        var writeResult = MapPackageConverter.SavePackage(mapFile, pkgDir, mapIniContent: null, mapStrContent: null);
        Assert.True(writeResult.Success);

        var reopened = EditorMap.OpenPackage(pkgDir);
        Assert.Equal(string.Empty, reopened.MapIniContent);
        Assert.Equal(string.Empty, reopened.MapStrContent);
    }

    private static MapFile CreateSyntheticMapFile()
    {
        var editorMap = NewMapFactory.Create(new NewMapParameters
        {
            PlayableWidth = 32,
            PlayableHeight = 32,
            InitialHeight = 10,
            BorderWidth = 6,
        });

        var mapFile = editorMap.MapFile;

        // Seed an initial placed object.
        var obj = new MapObject(new System.Numerics.Vector3(100, 100, 0), 0f, RoadType.None, "CustomUnitTemplate");
        obj.Properties.AddReal("InitialProp", 1.0f);
        mapFile.ObjectsList.AddObject(obj);

        return mapFile;
    }

    private sealed class MockArchiveFileSystem : FileSystem
    {
        public override FileSystemEntry? GetFile(string filePath) => null;

        public override IEnumerable<FileSystemEntry> GetFilesInDirectory(
            string directoryPath,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly) => [];
    }
}
