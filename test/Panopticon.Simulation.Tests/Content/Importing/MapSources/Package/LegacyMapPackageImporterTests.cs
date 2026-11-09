#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Xunit;

namespace Panopticon.Tests.Content.Importing.MapSources.Package;

/// <summary>
/// Unit and negative integration tests for <see cref="LegacyMapPackageImporter"/> and <see cref="PackageReader"/> (WI-H3-03).
/// </summary>
public sealed class LegacyMapPackageImporterTests
{
    [Fact]
    public void ImportAndReadBackSucceeds()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        var packageDir = Path.Combine(tempRoot, "valid-package");

        try
        {
            var interchange = BuildValidInterchange();
            var writeResult = LegacyMapPackageImporter.Import(interchange, packageDir);

            Assert.True(writeResult.Success);
            Assert.Empty(writeResult.Diagnostics);

            var reader = new PackageReader(packageDir);
            var reloaded = reader.Read();

            Assert.NotNull(reloaded);
            Assert.Equal(MapSourceKind.H3Package, reloaded.Identity.SourceKind);
            Assert.Equal(interchange.Identity.Name, reloaded.Identity.Name);
            Assert.Equal(interchange.Identity.LegacyGlobalVersion, reloaded.Identity.LegacyGlobalVersion);
            Assert.Equal(interchange.Layout.LatticeWidth, reloaded.Layout.LatticeWidth);
            Assert.Equal(interchange.Layout.LatticeHeight, reloaded.Layout.LatticeHeight);
            Assert.Single(reloaded.Layout.Chunks);
            Assert.Single(reloaded.RetainedSections);
            Assert.Equal(interchange.RetainedSections[0].SectionName, reloaded.RetainedSections[0].SectionName);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void PackageOnDisk_WithUnknownPackageVersion_ThrowsMapImportException()
    {
        var (tempRoot, packageDir) = CreateStagedPackage();

        try
        {
            var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
            var json = File.ReadAllText(manifestPath);
            var tampered = json.Replace($"\"packageVersion\": {MapPackageFormat.CurrentPackageVersion}", "\"packageVersion\": 999");
            File.WriteAllText(manifestPath, tampered);

            var reader = new PackageReader(packageDir);
            var ex = Assert.Throws<MapImportException>(() => reader.Read());

            Assert.Contains(ex.Diagnostics, d => d.Code == MapPackageDiagnostic.PackageVersion);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PackageOnDisk_WithUnknownInterchangeSchemaVersion_ThrowsMapImportException()
    {
        var (tempRoot, packageDir) = CreateStagedPackage();

        try
        {
            var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
            var json = File.ReadAllText(manifestPath);
            var tampered = json.Replace($"\"schemaVersion\": {MapInterchange.CurrentSchemaVersion}", "\"schemaVersion\": 999");
            File.WriteAllText(manifestPath, tampered);

            var reader = new PackageReader(packageDir);
            var ex = Assert.Throws<MapImportException>(() => reader.Read());

            Assert.Contains(ex.Diagnostics, d => d.Code == MapPackageDiagnostic.SchemaVersion);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PackageOnDisk_WithTamperedLayerFile_ThrowsMapImportException()
    {
        var (tempRoot, packageDir) = CreateStagedPackage();

        try
        {
            var heightsFile = Path.Combine(packageDir, "layers", "heights_0_0.bin");
            var bytes = File.ReadAllBytes(heightsFile);
            bytes[16] ^= 0xFF;
            File.WriteAllBytes(heightsFile, bytes);

            var reader = new PackageReader(packageDir);
            var ex = Assert.Throws<MapImportException>(() => reader.Read());

            Assert.Contains(ex.Diagnostics, d => d.Code == MapPackageChunkDiagnostic.DigestMismatch);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PackageOnDisk_WithMissingChunkFile_ThrowsMapImportException()
    {
        var (tempRoot, packageDir) = CreateStagedPackage();

        try
        {
            var heightsFile = Path.Combine(packageDir, "layers", "heights_0_0.bin");
            File.Delete(heightsFile);

            var reader = new PackageReader(packageDir);
            var ex = Assert.Throws<MapImportException>(() => reader.Read());

            Assert.Contains(ex.Diagnostics, d => d.Code == MapPackageChunkDiagnostic.FileMissing);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PackageOnDisk_WithSymlinkedLayerFile_ThrowsMapImportException()
    {
        var (tempRoot, packageDir) = CreateStagedPackage();

        try
        {
            var heightsFile = Path.Combine(packageDir, "layers", "heights_0_0.bin");
            var outsideFile = Path.Combine(tempRoot, "outside.bin");
            File.Move(heightsFile, outsideFile);
            File.CreateSymbolicLink(heightsFile, outsideFile);

            var reader = new PackageReader(packageDir);
            var ex = Assert.Throws<MapImportException>(() => reader.Read());

            Assert.Contains(ex.Diagnostics, d => d.Code == MapPackageChunkDiagnostic.SymlinkRejected);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PackageOnDisk_WithOversizedDeclaredByteLength_ThrowsMapImportException()
    {
        var (tempRoot, packageDir) = CreateStagedPackage();

        try
        {
            var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
            var json = File.ReadAllText(manifestPath);
            var expectedSize = 131 * 131 * 4;
            var tampered = json.Replace($"\"byteLength\": {expectedSize}", $"\"byteLength\": {expectedSize + 1000}");
            File.WriteAllText(manifestPath, tampered);

            var reader = new PackageReader(packageDir);
            var ex = Assert.Throws<MapImportException>(() => reader.Read());

            Assert.Contains(ex.Diagnostics, d => d.Code == MapPackageDiagnostic.LayerLength);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PackageOnDisk_WithMissingManifest_ThrowsMapImportException()
    {
        var (tempRoot, packageDir) = CreateStagedPackage();

        try
        {
            var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
            File.Delete(manifestPath);

            var reader = new PackageReader(packageDir);
            var ex = Assert.Throws<MapImportException>(() => reader.Read());

            Assert.Contains(ex.Diagnostics, d => d.Code == MapPackageChunkDiagnostic.FileMissing);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Import_WithInvalidInterchange_ReturnsFailureDiagnostics()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        var packageDir = Path.Combine(tempRoot, "invalid-package");

        try
        {
            var valid = BuildValidInterchange();
            var invalid = valid with { SchemaVersion = 999 };

            var result = LegacyMapPackageImporter.Import(invalid, packageDir);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.Code == MapInterchangeDiagnostic.SchemaVersion);
            Assert.False(Directory.Exists(packageDir));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ImporterAndReaderValidateArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new LegacyMapPackageImporter(null!, "/tmp/test"));
        Assert.Throws<ArgumentException>(() => new LegacyMapPackageImporter(BuildValidInterchange(), ""));
        Assert.Throws<ArgumentNullException>(() => LegacyMapPackageImporter.Import(null!, "/tmp/test"));
        Assert.Throws<ArgumentException>(() => LegacyMapPackageImporter.Import(BuildValidInterchange(), ""));

        Assert.Throws<ArgumentException>(() => new PackageReader(""));
        Assert.Throws<ArgumentException>(() => new PackageReader("   "));
    }

    [Fact]
    public void PackageOnDisk_WithOversizedManifest_ThrowsMapImportExceptionWithoutReadingFull()
    {
        // MapPackageFormat.MaxManifestBytes is a fixed constant (64 MiB).
        // Writing a sparse file just over the cap (64 MiB + 1 byte) in a temp directory
        // allocates no actual disk storage and completes instantaneously, ensuring
        // PackageReader rejects the oversized manifest before attempting a full read.
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        var packageDir = Path.Combine(tempRoot, "oversized-manifest-package");
        Directory.CreateDirectory(packageDir);

        try
        {
            var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
            using (var fs = File.Create(manifestPath))
            {
                fs.SetLength(MapPackageFormat.MaxManifestBytes + 1);
            }

            var reader = new PackageReader(packageDir);
            var ex = Assert.Throws<MapImportException>(() => reader.Read());

            Assert.Contains(ex.Diagnostics, d => d.Code == MapPackageDiagnostic.ResourceLimitExceeded);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void DuplicateRetainedSectionName_DirectoryWriterRejectsBeforeCreatingAnyFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        var packageDir = Path.Combine(tempRoot, "dup-retained-target");

        try
        {
            var valid = BuildValidInterchange();
            const string sampleSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var manifest = new MapPackageManifest(
                MapPackageFormat.PackageFormatName,
                MapPackageFormat.CurrentPackageVersion,
                MapInterchange.CurrentSchemaVersion,
                MapPackageIdentity.FromModel(valid.Identity),
                MapPackageTerrainLayout.FromModel(valid.Layout),
                MapPackageBlendModel.FromModel(valid.Blend),
                [],
                [],
                MapPackageProvenance.FromModel(valid.Provenance),
                [],
                [
                    new MapPackageRetainedEntry("GlobalVersion", 1, "retained/v1.bin", 4, sampleSha),
                    new MapPackageRetainedEntry("GlobalVersion", 1, "retained/v2.bin", 4, sampleSha),
                ]);

            var writeResult = MapPackageDirectoryWriter.Write(packageDir, manifest, new DummyPayloadProvider());

            Assert.False(writeResult.Success);
            Assert.Contains(writeResult.Diagnostics, d => d.Code == MapPackageDiagnostic.RetainedNameDuplicate);
            Assert.False(Directory.Exists(packageDir));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void UppercaseModelDigest_ImportAndReadBackNormalizesToLowercase()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        var packageDir = Path.Combine(tempRoot, "uppercase-digest-package");

        try
        {
            var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var upperSha = Convert.ToHexString(SHA256.HashData(bytes)).ToUpperInvariant();
            var section = new RetainedSection("GlobalVersion", 8, bytes, upperSha);

            var valid = BuildValidInterchange();
            var interchange = new MapInterchange(
                valid.SchemaVersion,
                valid.Identity,
                valid.Layout,
                valid.Blend,
                valid.Objects,
                valid.Waypoints,
                valid.Provenance,
                valid.Report,
                [section]);

            var writeResult = LegacyMapPackageImporter.Import(interchange, packageDir);
            Assert.True(writeResult.Success);

            var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
            var json = File.ReadAllText(manifestPath);
            var (manifest, _) = MapPackageManifestJson.Parse(json);
            Assert.NotNull(manifest);
            Assert.Single(manifest!.RetainedSections);
            Assert.Equal(upperSha.ToLowerInvariant(), manifest.RetainedSections[0].Sha256);

            var reader = new PackageReader(packageDir);
            var reloaded = reader.Read();
            Assert.NotNull(reloaded);
            Assert.Single(reloaded.RetainedSections);
            Assert.Equal(upperSha.ToLowerInvariant(), reloaded.RetainedSections[0].Sha256);
            Assert.True(bytes.AsSpan().SequenceEqual(reloaded.RetainedSections[0].Bytes.Span));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("bogus")]
    public void PackageOnDisk_WithTamperedNodataPolicy_ThrowsMapImportException(string badPolicy)
    {
        var (tempRoot, packageDir) = CreateStagedPackage();

        try
        {
            var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
            var json = File.ReadAllText(manifestPath);
            var tampered = json.Replace("\"nodataPolicy\": \"Fail\"", $"\"nodataPolicy\": \"{badPolicy}\"");
            File.WriteAllText(manifestPath, tampered);

            var reader = new PackageReader(packageDir);
            var ex = Assert.Throws<MapImportException>(() => reader.Read());

            Assert.Contains(ex.Diagnostics, d => d.Code == MapPackageDiagnostic.NodataPolicy);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("9")]
    [InlineData("bogus")]
    public void PackageOnDisk_WithTamperedSourceKind_ThrowsMapImportException(string badKind)
    {
        var (tempRoot, packageDir) = CreateStagedPackage();

        try
        {
            var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
            var json = File.ReadAllText(manifestPath);
            var tampered = json.Replace("\"sourceKind\": \"LegacyMap\"", $"\"sourceKind\": \"{badKind}\"");
            File.WriteAllText(manifestPath, tampered);

            var reader = new PackageReader(packageDir);
            var ex = Assert.Throws<MapImportException>(() => reader.Read());

            Assert.Contains(ex.Diagnostics, d => d.Code == MapPackageDiagnostic.IdentitySourceKind);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PackageOnDisk_WithLegacyMapSourceKind_ReturnsH3PackageOnRead()
    {
        var (tempRoot, packageDir) = CreateStagedPackage();

        try
        {
            var reader = new PackageReader(packageDir);
            var reloaded = reader.Read();

            Assert.NotNull(reloaded);
            Assert.Equal(MapSourceKind.H3Package, reloaded.Identity.SourceKind);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class DummyPayloadProvider : IMapPackagePayloadProvider
    {
        public float[] GetHeights(MapPackageLayerEntry entry) => [];
        public byte[] GetMaterials(MapPackageLayerEntry entry) => [];
        public byte[] GetMasks(MapPackageLayerEntry entry) => [];
        public byte[] GetImagery(MapPackageLayerEntry entry) => [];
        public byte[] GetRetainedSection(MapPackageRetainedEntry entry) => [];
    }

    private static (string TempRoot, string PackageDir) CreateStagedPackage()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        var packageDir = Path.Combine(tempRoot, "package");
        Directory.CreateDirectory(tempRoot);

        var interchange = BuildValidInterchange();
        var writeResult = LegacyMapPackageImporter.Import(interchange, packageDir);
        Assert.True(writeResult.Success);

        return (tempRoot, packageDir);
    }

    private static MapInterchange BuildValidInterchange()
    {
        const int latticePoints = 129;
        const int coreExtent = 129;
        const int storedExtent = coreExtent + 2;

        var heights = new float[storedExtent * storedExtent];
        Array.Fill(heights, 10.0f);

        var materials = new byte[coreExtent * coreExtent];
        var masks = new byte[coreExtent * coreExtent];

        var chunk = new Chunk(
            new ChunkKey(0, 0),
            new HeightLayer(heights),
            new MaterialLayer(materials),
            new MaskLayer(masks),
            null);

        var layout = new TerrainLayout(
            new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            latticePoints,
            latticePoints,
            1,
            1,
            0,
            null!,
            MapNodataPolicy.Fail,
            3,
            latticePoints * latticePoints,
            [new MapTerrainBorder(0, 0, latticePoints - 1, latticePoints - 1)],
            [chunk]);

        var blend = new BlendModel([
            new MaterialPaletteEntry(0, "grass", new AssetReference("terrain", "grass")),
        ]);

        var objects = new PlacedObjectSet([
            new PlacedObject(
                "obj-0",
                new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(20, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineZ),
                0.0,
                new AssetReference("legacy", "AmericaVehicleCrusader"),
                0u,
                new Dictionary<string, MapPropertyValue> { ["team"] = new MapPropertyValue.AsciiStringValue("america") }),
        ]);

        var waypoints = new WaypointSet([
            new Waypoint(
                "wp-0",
                "StartPoint",
                new MapQuantity(15, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(25, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineZ)),
        ]);

        var provenance = new Provenance(null, [], []);
        var report = new ImportReport([], [], 0, 0, []);
        var retainedBytes = new byte[] { 1, 2, 3, 4 };
        var retained = new RetainedSection("GlobalVersion", 8, retainedBytes, Sha256Hex(retainedBytes));

        return new MapInterchange(
            MapInterchange.CurrentSchemaVersion,
            new MapIdentity("TestMap", MapSourceKind.LegacyMap, 8),
            layout,
            blend,
            objects,
            waypoints,
            provenance,
            report,
            [retained]);
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
