#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.FileFormats;
using Panopticon.Logic.Map;
using Panopticon.Mathematics;
using Panopticon.Tests.Data;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Data.Map;

public sealed class PolygonTriggerVersionMatrixTests
{
    private static byte[] CraftPolygonTriggersSection(
        ushort version,
        string name,
        string? layerName,
        uint triggerId,
        bool isWater,
        bool isRiver,
        uint riverStart,
        Point3D[] points,
        bool includeDefaultWaterForV1 = false)
    {
        using var payloadStream = new MemoryStream();
        using var payloadWriter = new BinaryWriter(payloadStream);

        var triggerCount = includeDefaultWaterForV1 && version == 1 ? 2u : 1u;
        payloadWriter.Write(triggerCount);

        // Write trigger 1
        WriteTriggerFields(payloadWriter, version, name, layerName, triggerId, isWater, isRiver, riverStart, points);

        if (includeDefaultWaterForV1 && version == 1)
        {
            var defaultWater = PolygonTrigger.CreateDefaultWaterArea(triggerId + 1);
            WriteTriggerFields(payloadWriter, version, defaultWater.Name, defaultWater.LayerName, defaultWater.TriggerId, defaultWater.IsWater, defaultWater.IsRiver, defaultWater.RiverStart, defaultWater.Points);
        }

        var payloadBytes = payloadStream.ToArray();

        using var assetStream = new MemoryStream();
        using var assetWriter = new BinaryWriter(assetStream);

        assetWriter.Write(version);
        assetWriter.Write((uint)payloadBytes.Length);
        assetWriter.Write(payloadBytes);

        return assetStream.ToArray();
    }

    private static void WriteTriggerFields(
        BinaryWriter writer,
        ushort version,
        string name,
        string? layerName,
        uint triggerId,
        bool isWater,
        bool isRiver,
        uint riverStart,
        Point3D[] points)
    {
        writer.WriteUInt16PrefixedAsciiString(name);

        if (version >= 4)
        {
            writer.WriteUInt16PrefixedAsciiString(layerName);
        }

        writer.Write(triggerId);

        if (version >= 2)
        {
            writer.Write(isWater);
        }

        if (version >= 3)
        {
            writer.Write(isRiver);
            writer.Write(riverStart);
        }

        writer.Write((uint)points.Length);
        foreach (var point in points)
        {
            writer.Write(point);
        }
    }

    [Theory]
    [InlineData((ushort)1)]
    [InlineData((ushort)2)]
    [InlineData((ushort)3)]
    [InlineData((ushort)4)]
    public void PolygonTriggers_VersionMatrix_FieldPresenceAndAbsence(ushort version)
    {
        var points = new[]
        {
            new Point3D(0, 0, 0),
            new Point3D(100, 0, 0),
            new Point3D(100, 100, 0)
        };

        var craftedBytes = CraftPolygonTriggersSection(
            version,
            name: "TriggerAreaAlpha",
            layerName: "LayerAlpha",
            triggerId: 42,
            isWater: true,
            isRiver: true,
            riverStart: 2,
            points);

        using var readStream = new MemoryStream(craftedBytes);
        using var reader = new BinaryReader(readStream);
        var asset = PolygonTriggers.Parse(reader, new MapParseContext(new AssetNameCollection()));

        Assert.Equal(version, asset.Version);
        var trigger = asset.Triggers[0];
        Assert.Equal("TriggerAreaAlpha", trigger.Name);
        Assert.Equal(42u, trigger.TriggerId);
        Assert.Equal(3, trigger.Points.Length);

        if (version >= 4)
        {
            Assert.Equal("LayerAlpha", trigger.LayerName);
        }
        else
        {
            Assert.Null(trigger.LayerName);
        }

        if (version >= 2)
        {
            Assert.True(trigger.IsWater);
        }
        else
        {
            Assert.False(trigger.IsWater);
        }

        if (version >= 3)
        {
            Assert.True(trigger.IsRiver);
            Assert.Equal(2u, trigger.RiverStart);
        }
        else
        {
            Assert.False(trigger.IsRiver);
            Assert.Equal(0u, trigger.RiverStart);
        }

        if (version == 1)
        {
            Assert.Equal(2, asset.Triggers.Count);
            Assert.Equal("AutoAddedWaterAreaTrigger", asset.Triggers[1].Name);
        }
        else
        {
            Assert.Single(asset.Triggers);
        }
    }

    [Theory]
    [InlineData((ushort)1)]
    [InlineData((ushort)2)]
    [InlineData((ushort)3)]
    [InlineData((ushort)4)]
    public void PolygonTriggers_ByteIdenticalReserialization_AfterEdit(ushort version)
    {
        var points = new[]
        {
            new Point3D(10, 20, 30),
            new Point3D(40, 50, 60),
            new Point3D(70, 80, 90)
        };

        var initialBytes = CraftPolygonTriggersSection(
            version,
            name: "EditableTrigger",
            layerName: "LayerBeta",
            triggerId: 99,
            isWater: true,
            isRiver: true,
            riverStart: 1,
            points,
            includeDefaultWaterForV1: true);

        using var stream1 = new MemoryStream(initialBytes);
        using var reader1 = new BinaryReader(stream1);
        var asset1 = PolygonTriggers.Parse(reader1, new MapParseContext(new AssetNameCollection()));

        using var initialSerializedStream = new MemoryStream();
        using var initialSerializedWriter = new BinaryWriter(initialSerializedStream);
        asset1.WriteTo(initialSerializedWriter);
        var baselineBytes = initialSerializedStream.ToArray();

        if (version >= 2)
        {
            Assert.Equal(initialBytes, baselineBytes);
        }

        var originalPoint0 = asset1.Triggers[0].Points[0];
        var editedPoint0 = new Point3D(999, 888, 777);
        asset1.Triggers[0].Points[0] = editedPoint0;

        using var editedStream = new MemoryStream();
        using var editedWriter = new BinaryWriter(editedStream);
        asset1.WriteTo(editedWriter);
        var editedBytes = editedStream.ToArray();

        using var reparseStream = new MemoryStream(editedBytes);
        using var reparseReader = new BinaryReader(reparseStream);
        var reparsedAsset = PolygonTriggers.Parse(reparseReader, new MapParseContext(new AssetNameCollection()));

        Assert.Equal(editedPoint0, reparsedAsset.Triggers[0].Points[0]);

        if (version >= 2)
        {
            using var reparseSerializedStream = new MemoryStream();
            using var reparseSerializedWriter = new BinaryWriter(reparseSerializedStream);
            reparsedAsset.WriteTo(reparseSerializedWriter);
            var secondWriteBytes = reparseSerializedStream.ToArray();

            Assert.Equal(editedBytes, secondWriteBytes);
        }

        asset1.Triggers[0].Points[0] = originalPoint0;
        using var revertedStream = new MemoryStream();
        using var revertedWriter = new BinaryWriter(revertedStream);
        asset1.WriteTo(revertedWriter);
        var revertedBytes = revertedStream.ToArray();

        Assert.Equal(baselineBytes, revertedBytes);
    }

    [Fact]
    public void MapPackage_MaterializedTriggers_LegacyReExport_ReparsesSuccessfully()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        using var fileSystem = installation.CreateFileSystem();
        var entry = fileSystem.GetFile("maps/deepdivide/deepdivide.map");
        Assert.NotNull(entry);

        var originalMap = MapFile.FromFileSystemEntry(entry);
        Assert.NotNull(originalMap.PolygonTriggers);
        var expectedVersion = originalMap.PolygonTriggers.Version;
        Assert.True(expectedVersion < 4, $"Expected legacy version < 4 with gated fields, but was {expectedVersion}");
        Assert.NotEmpty(originalMap.PolygonTriggers.Triggers);

        var originalTriggerCount = originalMap.PolygonTriggers.Triggers.Count;

        // Re-export legacy bytes from the map containing version < 4 triggers
        // (Bug 998 reproducer: previously wrote version 4 fields unconditionally, breaking reparse)
        using var legacyStream = new MemoryStream();
        originalMap.WriteTo(legacyStream);
        var legacyBytes = legacyStream.ToArray();

        using var reparseStream = new MemoryStream(legacyBytes);
        var reparsedMap = MapFile.FromStream(reparseStream);

        Assert.NotNull(reparsedMap.PolygonTriggers);
        Assert.Equal(expectedVersion, reparsedMap.PolygonTriggers.Version);
        Assert.Equal(originalTriggerCount, reparsedMap.PolygonTriggers.Triggers.Count);

        for (var i = 0; i < originalTriggerCount; i++)
        {
            var expected = originalMap.PolygonTriggers.Triggers[i];
            var actual = reparsedMap.PolygonTriggers.Triggers[i];

            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.TriggerId, actual.TriggerId);
            Assert.Equal(expected.Points.Length, actual.Points.Length);
            for (var p = 0; p < expected.Points.Length; p++)
            {
                Assert.Equal(expected.Points[p], actual.Points[p]);
            }
        }

        // Also test package-materialized triggers via RuntimeBridge:
        var v4Triggers = new MapPolygonTriggersRecord(
            4,
            [
                new MapPolygonTriggerRecord(
                    "PackageTriggerAlpha",
                    "PackageLayerAlpha",
                    777,
                    true,
                    false,
                    0,
                    [new System.Numerics.Vector3(0, 0, 0), new System.Numerics.Vector3(50, 0, 0), new System.Numerics.Vector3(50, 50, 0)],
                    false,
                    System.Numerics.Vector2.Zero,
                    null, null, null, null, null, null, 0, 0, 0, 0, 1f)
            ]);

        var baseInterchange = new LegacyMapReader(originalMap, entry.FilePath).Read();
        var pkgInterchange = baseInterchange with { PolygonTriggers = v4Triggers };

        var materializedFromPackage = RuntimeBridge.MaterializePolygonTriggers(pkgInterchange);
        Assert.NotNull(materializedFromPackage);
        Assert.Equal((ushort)4, materializedFromPackage.Version);
        Assert.Single(materializedFromPackage.Triggers);

        originalMap.PolygonTriggers = materializedFromPackage;

        using var pkgLegacyStream = new MemoryStream();
        originalMap.WriteTo(pkgLegacyStream);
        var pkgLegacyBytes = pkgLegacyStream.ToArray();

        using var pkgReparseStream = new MemoryStream(pkgLegacyBytes);
        var pkgReparsedMap = MapFile.FromStream(pkgReparseStream);

        Assert.NotNull(pkgReparsedMap.PolygonTriggers);
        Assert.Equal((ushort)4, pkgReparsedMap.PolygonTriggers.Version);
        var reparsedPkgTrigger = Assert.Single(pkgReparsedMap.PolygonTriggers.Triggers);
        Assert.Equal("PackageTriggerAlpha", reparsedPkgTrigger.Name);
        Assert.Equal("PackageLayerAlpha", reparsedPkgTrigger.LayerName);
        Assert.Equal(777u, reparsedPkgTrigger.TriggerId);
        Assert.True(reparsedPkgTrigger.IsWater);
        Assert.Equal(3, reparsedPkgTrigger.Points.Length);
    }

    [Theory]
    [InlineData((ushort)1)]
    [InlineData((ushort)2)]
    [InlineData((ushort)3)]
    [InlineData((ushort)4)]
    public void ReadRetainedPolygonTriggers_VersionMatrix_FieldPresenceAndAbsence(ushort version)
    {
        var points = new[]
        {
            new Point3D(0, 0, 0),
            new Point3D(100, 0, 0),
            new Point3D(100, 100, 0)
        };

        var craftedBytes = CraftPolygonTriggersSection(
            version,
            name: "RetainedTrigger",
            layerName: "RetainedLayer",
            triggerId: 42,
            isWater: true,
            isRiver: true,
            riverStart: 2,
            points,
            includeDefaultWaterForV1: false);

        var retained = new RetainedSection(
            PolygonTriggers.AssetName,
            version,
            craftedBytes,
            "sha256");

        var asset = MapBackendReaders.ReadRetainedPolygonTriggers(retained);
        Assert.Equal(version, asset.Version);
        var trigger = Assert.Single(asset.Triggers);
        Assert.Equal("RetainedTrigger", trigger.Name);
        Assert.Equal(42u, trigger.TriggerId);
        Assert.Equal(3, trigger.Points.Length);

        if (version >= 4)
        {
            Assert.Equal("RetainedLayer", trigger.LayerName);
        }
        else
        {
            Assert.Null(trigger.LayerName);
        }

        if (version >= 2)
        {
            Assert.True(trigger.IsWater);
        }
        else
        {
            Assert.False(trigger.IsWater);
        }

        if (version >= 3)
        {
            Assert.True(trigger.IsRiver);
            Assert.Equal(2u, trigger.RiverStart);
        }
        else
        {
            Assert.False(trigger.IsRiver);
            Assert.Equal(0u, trigger.RiverStart);
        }
    }

    [Fact]
    public void ConvertedMapPackage_GeneralsV1Source_MaterializesPolygonTriggersViaRetainedBackend()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        using var fileSystem = installation.CreateFileSystem();
        var entry = fileSystem.GetFile("maps/alpine assault/alpine assault.map");
        Assert.NotNull(entry);

        var originalMap = MapFile.FromFileSystemEntry(entry);
        Assert.NotNull(originalMap.PolygonTriggers);
        Assert.True(originalMap.PolygonTriggers.Version < 4, $"Expected Generals legacy version < 4, but was {originalMap.PolygonTriggers.Version}");
        Assert.NotEmpty(originalMap.PolygonTriggers.Triggers);

        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTest_H5V1PolygonTriggers_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceMapPath = Path.Combine(tempRoot, "alpine assault.map");
            using (var sourceStream = entry.Open())
            using (var fileStream = File.Create(sourceMapPath))
            {
                sourceStream.CopyTo(fileStream);
            }

            var packageDir = Path.Combine(tempRoot, "package");
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["convert-map", "--input", Path.GetFullPath(sourceMapPath), "--out", Path.GetFullPath(packageDir)],
                outWriter,
                errWriter,
                CancellationToken.None);

            Assert.True(exitCode == 0, $"CLI conversion failed: {errWriter}{outWriter}");

            var packageReader = new PackageReader(packageDir);
            var interchange = packageReader.Read();

            Assert.Null(interchange.PolygonTriggers);
            var retained = Assert.Single(interchange.RetainedSections, s => s.SectionName == PolygonTriggers.AssetName);
            Assert.Equal((int)originalMap.PolygonTriggers.Version, retained.SectionVersion);

            var reloadedMap = MapFile.FromInterchange(interchange);
            Assert.NotNull(reloadedMap.PolygonTriggers);
            Assert.Equal(originalMap.PolygonTriggers.Version, reloadedMap.PolygonTriggers.Version);
            Assert.Equal(originalMap.PolygonTriggers.Triggers.Count, reloadedMap.PolygonTriggers.Triggers.Count);

            for (var i = 0; i < originalMap.PolygonTriggers.Triggers.Count; i++)
            {
                var expected = originalMap.PolygonTriggers.Triggers[i];
                var actual = reloadedMap.PolygonTriggers.Triggers[i];

                Assert.Equal(expected.Name, actual.Name);
                Assert.Equal(expected.TriggerId, actual.TriggerId);
                Assert.Equal(expected.Points.Length, actual.Points.Length);
                for (var p = 0; p < expected.Points.Length; p++)
                {
                    Assert.Equal(expected.Points[p], actual.Points[p]);
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
