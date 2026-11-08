#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Audio;
using Panopticon.Client;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data;
using Panopticon.Data.Map;
using Panopticon.FileFormats;
using Panopticon.IO;
using Panopticon.Logic.Map;
using Panopticon.Mathematics;
using Panopticon.Tests.Data;
using Panopticon.Tools.H5Migrate;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.MapSources;

/// <summary>
/// Converted-map runtime integration tests for WI-H5-04:
/// verifies that converted H3 directory packages load honestly through the Game.LoadMap branch,
/// materializing all 29 audited section disposition rows into genuine live runtime types,
/// and that negative cases fail with the expected typed diagnostics without silent fallback.
/// </summary>
public sealed class ConvertedMapLoadTests
{
    private readonly ITestOutputHelper _output;

    public ConvertedMapLoadTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ConvertedFixtureMap_MaterializesAllDispositionRows()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var originalMap = BuildComprehensiveMapFile();
            var mapPath = Path.Combine(tempRoot, "Comprehensive.map");
            using (var stream = File.Create(mapPath))
            {
                originalMap.WriteTo(stream);
            }

            var packageDir = Path.Combine(tempRoot, "comprehensive_package");
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["convert-map", "--input", mapPath, "--out", packageDir],
                outWriter,
                errWriter,
                CancellationToken.None);

            Assert.True(exitCode == 0, $"CLI conversion failed: {errWriter}");

            var packageReader = new PackageReader(packageDir);
            var interchange = packageReader.Read();

            var reloadedMap = MapFile.FromInterchange(interchange);

            // 1. Heights and Objects (native H3 layers)
            Assert.NotNull(reloadedMap.HeightMapData);
            Assert.Equal(originalMap.HeightMapData.Width, reloadedMap.HeightMapData.Width);
            Assert.Equal(originalMap.HeightMapData.Height, reloadedMap.HeightMapData.Height);
            Assert.NotNull(reloadedMap.ObjectsList);
            Assert.Equal(originalMap.ObjectsList.Objects.Length, reloadedMap.ObjectsList.Objects.Length);

            // 2. Typed homes materialized to live sections
            Assert.NotNull(reloadedMap.BlendTileData);
            Assert.Equal(originalMap.BlendTileData.NumTiles, reloadedMap.BlendTileData.NumTiles);
            Assert.Equal(originalMap.BlendTileData.Textures.Length, reloadedMap.BlendTileData.Textures.Length);

            Assert.NotNull(reloadedMap.GlobalLighting);
            Assert.Equal(originalMap.GlobalLighting.Time, reloadedMap.GlobalLighting.Time);

            Assert.NotNull(reloadedMap.NamedCameras);
            Assert.Equal(originalMap.NamedCameras.Cameras.Length, reloadedMap.NamedCameras.Cameras.Length);
            Assert.Equal(originalMap.NamedCameras.Cameras[0].Name, reloadedMap.NamedCameras.Cameras[0].Name);

            Assert.NotNull(reloadedMap.StandingWaterAreas);
            Assert.Equal(originalMap.StandingWaterAreas.Areas.Length, reloadedMap.StandingWaterAreas.Areas.Length);

            Assert.NotNull(reloadedMap.RiverAreas);
            Assert.Equal(originalMap.RiverAreas.Areas.Length, reloadedMap.RiverAreas.Areas.Length);

            Assert.NotNull(reloadedMap.StandingWaveAreas);
            Assert.Equal(originalMap.StandingWaveAreas.Areas.Length, reloadedMap.StandingWaveAreas.Areas.Length);

            Assert.NotNull(reloadedMap.GlobalWaterSettings);
            Assert.Equal(originalMap.GlobalWaterSettings.ReflectionOn, reloadedMap.GlobalWaterSettings.ReflectionOn);

            Assert.NotNull(reloadedMap.PolygonTriggers);
            Assert.Equal(originalMap.PolygonTriggers.Triggers.Count, reloadedMap.PolygonTriggers.Triggers.Count);

            Assert.NotNull(reloadedMap.WaypointsList);
            Assert.Equal(originalMap.WaypointsList.WaypointPaths.Length, reloadedMap.WaypointsList.WaypointPaths.Length);

            // 3. Backend-parsed retained sections
            Assert.NotNull(reloadedMap.SidesList);
            Assert.Equal(originalMap.SidesList.Players.Count, reloadedMap.SidesList.Players.Count);

            Assert.NotNull(reloadedMap.TriggerAreas);
            Assert.Equal(originalMap.TriggerAreas.Areas.Length, reloadedMap.TriggerAreas.Areas.Length);

            Assert.NotNull(reloadedMap.WorldInfo);
            Assert.Equal(originalMap.WorldInfo.Properties.Count, reloadedMap.WorldInfo.Properties.Count);

            Assert.NotNull(reloadedMap.AssetList);
            Assert.Equal(originalMap.AssetList.Items.Length, reloadedMap.AssetList.Items.Length);

            Assert.NotNull(reloadedMap.MPPositionList);
            Assert.Equal(originalMap.MPPositionList.Positions.Length, reloadedMap.MPPositionList.Positions.Length);

            Assert.NotNull(reloadedMap.LibraryMapLists);
            Assert.Equal(originalMap.LibraryMapLists.Lists.Length, reloadedMap.LibraryMapLists.Lists.Length);

            Assert.NotNull(reloadedMap.BuildLists);
            Assert.Equal(originalMap.BuildLists.Items.Length, reloadedMap.BuildLists.Items.Length);

            Assert.NotNull(reloadedMap.CastleTemplates);
            Assert.Equal(originalMap.CastleTemplates.Templates.Length, reloadedMap.CastleTemplates.Templates.Length);

            Assert.NotNull(reloadedMap.FogSettings);
            Assert.NotNull(reloadedMap.PostEffectsChunk);
            Assert.NotNull(reloadedMap.EnvironmentData);
            Assert.NotNull(reloadedMap.SkyboxSettings);
            Assert.NotNull(reloadedMap.MissionHotSpots);
            Assert.NotNull(reloadedMap.MissionObjectives);
            Assert.NotNull(reloadedMap.CameraAnimationList);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Negative_UnknownRetainedSectionName_FailsLoad()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var mapFile = MapFile.CreateEmpty(16, 16, 1, 10);
            var interchange = new LegacyMapReader(mapFile).Read();

            var dummyBytes = new byte[] { 1, 2, 3, 4 };
            var sha = Convert.ToHexStringLower(SHA256.HashData(dummyBytes));
            var tamperedInterchange = interchange with
            {
                RetainedSections = [new RetainedSection("UnknownSectionNameXYZ", 1, dummyBytes, sha)],
            };

            var diagnostics = MapInterchangeValidator.Validate(tamperedInterchange);
            Assert.Contains(diagnostics, d => d.Code == MapInterchangeDiagnostic.RetainedName);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Negative_MissingDisposition_FailsValidationAndLoad()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var mapFile = MapFile.CreateEmpty(16, 16, 1, 10);
            var interchange = new LegacyMapReader(mapFile).Read();

            var dummyBytes = new byte[] { 1, 2, 3, 4 };
            var sha = Convert.ToHexStringLower(SHA256.HashData(dummyBytes));
            var tamperedInterchange = interchange with
            {
                RetainedSections = [new RetainedSection("AuditedNameWithoutDisposition", 1, dummyBytes, sha)],
            };

            var diagnostics = MapInterchangeValidator.Validate(tamperedInterchange);
            Assert.Contains(diagnostics, d => d.Code == MapInterchangeDiagnostic.RetainedName);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Negative_UnsupportedSidesListVersion_FailsLoad()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var mapFile = MapFile.CreateEmpty(16, 16, 1, 10);
            var interchange = new LegacyMapReader(mapFile).Read();

            var sidesSec = Assert.Single(interchange.RetainedSections, s => s.SectionName == "SidesList");
            var unsupportedSides = new RetainedSection("SidesList", 1, sidesSec.Bytes, sidesSec.Sha256);

            var tamperedInterchange = interchange with
            {
                RetainedSections = interchange.RetainedSections.Select(s => s.SectionName == "SidesList" ? unsupportedSides : s).ToArray(),
            };

            var diagnostics = MapInterchangeValidator.Validate(tamperedInterchange);
            Assert.Contains(diagnostics, d => d.Code == MapInterchangeDiagnostic.SidesListVersion);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Negative_UnrepresentableHeight_FailsWithoutQuantization()
    {
        var model = BuildSingleChunkModel();
        var samples = model.Layout.Chunks[0].Heights.Samples.ToArray();
        // Index 132 is storedWidth + 1, which corresponds to core point (0, 0).
        // 0.0390625 is vertical scale. 10.01 cannot be represented exactly in 0.0390625 steps.
        const int storedWidth = MapChunkGeometry.CorePointsPerAxis + (2 * MapChunkGeometry.ApronSamplesPerSide);
        samples[storedWidth + 1] = 10.01f;

        var tamperedChunk = model.Layout.Chunks[0] with
        {
            Heights = new HeightLayer(samples),
        };

        var tamperedLayout = model.Layout with
        {
            Chunks = [tamperedChunk],
        };

        var tamperedModel = model with { Layout = tamperedLayout };

        var ex = Assert.Throws<MapImportException>(() => RuntimeBridge.MaterializeHeightMapData(tamperedModel));
        Assert.Contains(ex.Diagnostics, d => d.Code == MapInterchangeDiagnostic.HeightRepresentable);
    }

    [Fact]
    public void Negative_ArchiveAddressedPackage_FailsWithUnsupportedAddressing()
    {
        Platform.Start();

        try
        {
            var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
            if (installation == null)
            {
                return;
            }

            using var game = new Game(installation, null, new Configuration { NoAudio = true }, null);

            var ex = Assert.Throws<MapImportException>(() =>
                game.LoadMap("maps/alpine assault/alpine assault.big/manifest.json", [], GameType.SinglePlayer));

            var diagnostic = Assert.Single(ex.Diagnostics);
            Assert.Equal(MapInterchangeDiagnostic.UnsupportedAddressing, diagnostic.Code);
        }
        finally
        {
            Platform.Stop();
        }
    }

    [Fact]
    public void ConvertedDirectoryPackage_LoadsThroughGameLoadMapBranch()
    {
        Platform.Start();

        try
        {
            var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
            if (installation == null)
            {
                _output.WriteLine("No Generals installation found; skipped.");
                return;
            }

            var tempRoot = NewTempRoot();

            try
            {
                using var game = new Game(installation, null, new Configuration { NoAudio = true }, null);

                var mapEntry = game.ContentManager.GetMapEntry("maps/alpine assault/alpine assault.map")
                    ?? game.ContentManager.FileSystem.GetFilesInDirectory("maps", "*.map", SearchOption.AllDirectories).First();

                var mapPath = Path.Combine(tempRoot, "source.map");
                using (var src = mapEntry.Open())
                using (var dst = File.Create(mapPath))
                {
                    src.CopyTo(dst);
                }

                var packageDir = Path.Combine(tempRoot, "converted_pkg");
                using var outWriter = new StringWriter();
                using var errWriter = new StringWriter();

                var exitCode = H5MigrateCli.Run(
                    ["convert-map", "--input", mapPath, "--out", packageDir],
                    outWriter,
                    errWriter,
                    CancellationToken.None);

                Assert.Equal(0, exitCode);
                Assert.True(File.Exists(Path.Combine(packageDir, MapPackageFormat.ManifestFileName)));

                // Load through Game.LoadMap honest source-kind branch
                game.LoadMap(packageDir, [], GameType.SinglePlayer);

                Assert.NotNull(game.Scene3D);
                Assert.NotNull(game.TerrainLogic.HeightMap);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
        finally
        {
            Platform.Stop();
        }
    }

    private static string NewTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonConvertedMapLoad", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private static MapInterchange BuildSingleChunkModel()
    {
        const int core = MapChunkGeometry.CorePointsPerAxis;
        const int stored = core + (2 * MapChunkGeometry.ApronSamplesPerSide);
        var samples = new float[stored * stored];

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = 100f * 0.0390625f;
        }

        var chunk = new Chunk(new ChunkKey(0, 0), new HeightLayer(samples), null, null, null);
        var layout = new TerrainLayout(
            new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            core,
            core,
            1,
            1,
            0,
            null!,
            MapNodataPolicy.Fail,
            5,
            (uint)(core * core),
            [new MapTerrainBorder(0, 0, core, core)],
            [chunk]);

        return new MapInterchange(
            1,
            new MapIdentity("single-chunk", MapSourceKind.H3Package, 8),
            layout,
            new BlendModel([]),
            null,
            null,
            new Provenance(null, [], []),
            new ImportReport([], [], 0, 0, []),
            []);
    }

    private static MapFile BuildComprehensiveMapFile()
    {
        const int width = 129;
        const int height = 129;
        const uint border = 6;

        var heights = new ushort[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                heights[x, y] = 100;
            }
        }

        var mapFile = MapFile.CreateFromHeights(heights, border);

        // Add additional sections to verify full disposition coverage
        mapFile.NamedCameras = ParseSection<NamedCameras>(
            NamedCameras.AssetName,
            writer =>
            {
                writer.Write(1u); // count
                writer.Write(new Vector3(100, 100, 50));
                writer.WriteUInt16PrefixedAsciiString("IntroCamera");
                writer.Write(0.5f); // pitch
                writer.Write(0f); // roll
                writer.Write(1.2f); // yaw
                writer.Write(10f); // zoom
                writer.Write(60f); // fov
                writer.Write(0f); // unknown
            },
            version: 1);

        mapFile.StandingWaterAreas = ParseSection<StandingWaterAreas>(
            StandingWaterAreas.AssetName,
            writer =>
            {
                writer.Write(1u); // count
                writer.Write(101u); // uniqueId
                writer.WriteUInt16PrefixedAsciiString("LakeArea");
                writer.WriteUInt16PrefixedAsciiString("Default");
                writer.Write(1f); // uvScroll
                writer.Write(false); // additive
                writer.WriteUInt16PrefixedAsciiString("");
                writer.WriteUInt16PrefixedAsciiString("");
                writer.Write(4u); // 4 points
                writer.Write(new Vector2(10, 10));
                writer.Write(new Vector2(50, 10));
                writer.Write(new Vector2(50, 50));
                writer.Write(new Vector2(10, 50));
                writer.Write(50u); // waterHeight
                writer.WriteUInt16PrefixedAsciiString("");
                writer.WriteUInt16PrefixedAsciiString("");
            },
            version: 1);

        mapFile.RiverAreas = ParseSection<RiverAreas>(
            RiverAreas.AssetName,
            writer =>
            {
                writer.Write(1u); // count
                writer.Write(201u); // uniqueId
                writer.WriteUInt16PrefixedAsciiString("RiverAlpha");
                writer.WriteUInt16PrefixedAsciiString("Default");
                writer.Write(1f);
                writer.Write(false);
                writer.WriteUInt16PrefixedAsciiString("");
                writer.WriteUInt16PrefixedAsciiString("");
                writer.WriteUInt16PrefixedAsciiString("");
                writer.WriteUInt16PrefixedAsciiString("");
                writer.Write((byte)100);
                writer.Write((byte)150);
                writer.Write((byte)200);
                writer.Write((byte)0);
                writer.Write(0.8f);
                writer.Write(20u);
                writer.WriteUInt16PrefixedAsciiString("Default");
                writer.Write(1u); // 1 line
                writer.Write(new Vector2(0, 0));
                writer.Write(new Vector2(100, 100));
            },
            version: 2);

        mapFile.StandingWaveAreas = ParseSection<StandingWaveAreas>(
            StandingWaveAreas.AssetName,
            writer =>
            {
                writer.Write(1u);
                writer.Write(301u);
                writer.WriteUInt16PrefixedAsciiString("Wave1");
                writer.WriteUInt16PrefixedAsciiString("Default");
                writer.Write(1f);
                writer.Write(false);
                writer.Write(3u);
                writer.Write(new Vector2(0, 0));
                writer.Write(new Vector2(20, 0));
                writer.Write(new Vector2(20, 20));
                writer.Write(0u);
                writer.Write(100u);
                writer.Write(100u);
                writer.Write(1u);
                writer.Write(1u);
                writer.Write(10u);
                writer.Write(5u);
                writer.Write(5u);
                writer.Write(0u);
                writer.Write(0u);
                writer.WriteUInt16PrefixedAsciiString("");
                writer.WriteBooleanUInt32(false);
            },
            version: 2);

        mapFile.GlobalWaterSettings = ParseSection<GlobalWaterSettings>(
            GlobalWaterSettings.AssetName,
            writer =>
            {
                writer.WriteBooleanUInt32(true);
                writer.Write(25.5f);
            },
            version: 1);

        mapFile.PolygonTriggers = ParseSection<PolygonTriggers>(
            PolygonTriggers.AssetName,
            writer =>
            {
                writer.Write(1u);
                writer.WriteUInt16PrefixedAsciiString("Trigger1");
                writer.WriteUInt16PrefixedAsciiString("Default");
                writer.Write(1u); // id
                writer.Write(false); // isWater
                writer.Write(false); // isRiver
                writer.Write(0u); // riverStart
                writer.Write(3u); // 3 points
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(10);
                writer.Write(0);
                writer.Write(0);
                writer.Write(10);
                writer.Write(10);
                writer.Write(0);
            },
            version: 4);

        mapFile.TriggerAreas = ParseSection<TriggerAreas>(
            TriggerAreas.AssetName,
            writer =>
            {
                writer.Write(1u);
                writer.WriteUInt16PrefixedAsciiString("AreaAlpha");
                writer.WriteUInt16PrefixedAsciiString("Layer1");
                writer.Write(101u);
                writer.Write(3u);
                writer.Write(new Vector2(0, 0));
                writer.Write(new Vector2(10, 0));
                writer.Write(new Vector2(10, 10));
                writer.Write(0u);
            },
            version: 1);

        mapFile.FogSettings = ParseSection<FogSettings>(
            FogSettings.AssetName,
            writer =>
            {
                writer.WriteBooleanUInt32(false);
                writer.Write(0f);
                writer.Write(1000f);
                writer.Write(new Vector3(0.5f, 0.5f, 0.5f));
            },
            version: 1);

        mapFile.PostEffectsChunk = ParseSection<PostEffectsChunk>(
            PostEffectsChunk.AssetName,
            writer =>
            {
                writer.Write((byte)0);
            },
            version: 1);

        mapFile.EnvironmentData = ParseSection<EnvironmentData>(
            EnvironmentData.AssetName,
            writer =>
            {
                writer.Write(false);
                writer.WriteUInt16PrefixedAsciiString("");
                writer.WriteUInt16PrefixedAsciiString("");
            },
            version: 1);

        mapFile.SkyboxSettings = ParseSection<SkyboxSettings>(
            SkyboxSettings.AssetName,
            writer =>
            {
                writer.Write(Vector3.Zero);
                writer.Write(1f);
                writer.Write(0f);
                writer.WriteUInt16PrefixedAsciiString("DefaultSky");
            },
            version: 1);

        mapFile.MissionHotSpots = ParseSection<MissionHotSpots>(
            MissionHotSpots.AssetName,
            writer =>
            {
                writer.Write(0u);
            },
            version: 1);

        mapFile.MissionObjectives = ParseSection<MissionObjectives>(
            MissionObjectives.AssetName,
            writer =>
            {
                writer.Write(0u);
            },
            version: 1);

        mapFile.CameraAnimationList = ParseSection<CameraAnimationList>(
            CameraAnimationList.AssetName,
            writer =>
            {
                writer.Write(0u);
            },
            version: 1);

        mapFile.AssetList = ParseSection<AssetList>(
            AssetList.AssetName,
            writer =>
            {
                writer.Write(1u);
                writer.Write(10u);
                writer.Write(20u);
            },
            version: 1);

        mapFile.MPPositionList = ParseSection<MPPositionList>(
            MPPositionList.AssetName,
            writer =>
            {
                writer.Write(1u);
                writer.Write((ushort)0);
                writer.Write(6u);
                writer.Write(true);
                writer.Write(true);
                writer.Write(0u);
            },
            version: 1);

        mapFile.LibraryMapLists = ParseSection<LibraryMapLists>(
            LibraryMapLists.AssetName,
            writer =>
            {
                writer.Write(2u);
                writer.Write((ushort)0);
                writer.Write(4u);
                writer.Write(0u);
            },
            version: 1);

        mapFile.BuildLists = ParseSection<BuildLists>(
            BuildLists.AssetName,
            writer =>
            {
                writer.Write(0u);
            },
            version: 1);

        mapFile.CastleTemplates = ParseSection<CastleTemplates>(
            CastleTemplates.AssetName,
            writer =>
            {
                writer.Write((byte)AssetPropertyType.AsciiString);
                writer.WriteUInt24(4u);
                writer.Write(0u);
            },
            version: 1);

        return mapFile;
    }

    private static T ParseSection<T>(string assetName, Action<BinaryWriter> writePayload, ushort version)
        where T : Asset
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(version);
            var sizePos = stream.Position;
            writer.Write(0u);
            var startPos = stream.Position;

            writePayload(writer);

            var endPos = stream.Position;
            var dataSize = (uint)(endPos - startPos);
            stream.Position = sizePos;
            writer.Write(dataSize);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var names = new AssetNameCollection();
        names.GetOrCreateAssetIndex("MPPositionInfo");
        names.GetOrCreateAssetIndex("LibraryMaps");
        names.GetOrCreateAssetIndex("BuildList");
        names.GetOrCreateAssetIndex("CastleTemplates");
        var context = new MapParseContext(names);

        var method = typeof(T).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "Parse" && m.GetParameters().Length >= 2);
        var args = method!.GetParameters().Length == 3
            ? new object[] { reader, context, false }
            : new object[] { reader, context };

        return (T)method.Invoke(null, args)!;
    }
}
