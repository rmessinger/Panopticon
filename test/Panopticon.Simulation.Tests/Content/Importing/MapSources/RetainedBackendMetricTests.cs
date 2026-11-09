#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data.Map;
using Panopticon.FileFormats;
using Panopticon.Logic.Map;
using Panopticon.Mathematics;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Tests.Content.Importing.MapSources;

public sealed class RetainedBackendMetricTests
{
    private const float Tolerance = 1e-4f;
    private static readonly float S = (float)UnitConversionPolicy.GeneralsFeetToMeters;

    [Fact]
    public void WaterPolygonsAndLevels_ConvertFromLegacyToMeters()
    {
        // 1. PolygonTrigger
        var triggerBytes = CreatePolygonTriggerBytes("WaterTrig", 1, isWater: true, points: [new Point3D(100, 200, 7), new Point3D(300, 200, 7), new Point3D(200, 400, 7)]);
        using (var stream = new MemoryStream(triggerBytes))
        using (var reader = new BinaryReader(stream))
        {
            var trigger = PolygonTrigger.Parse(reader, version: 3)!;
            Assert.NotNull(trigger);
            Assert.Equal(100 * S, trigger.Points[0].X, Tolerance);
            Assert.Equal(200 * S, trigger.Points[0].Y, Tolerance);
            Assert.Equal(7 * S, trigger.Points[0].Z, Tolerance);
            Assert.Equal(300 * S, trigger.Points[1].X, Tolerance);
            Assert.Equal(200 * S, trigger.Points[1].Y, Tolerance);
            Assert.Equal(200 * S, trigger.Points[2].X, Tolerance);
            Assert.Equal(400 * S, trigger.Points[2].Y, Tolerance);
            Assert.True(trigger.Radius > 0);
        }

        // 2. StandingWaterArea
        var standingWaterBytes = CreateStandingWaterAreaBytes(1, "Lake", [new Vector2(100, 200), new Vector2(300, 200), new Vector2(200, 400)], waterHeight: 25);
        using (var stream = new MemoryStream(standingWaterBytes))
        using (var reader = new BinaryReader(stream))
        {
            var area = StandingWaterArea.Parse(reader);
            Assert.Equal(100 * S, area.Points[0].X, Tolerance);
            Assert.Equal(200 * S, area.Points[0].Y, Tolerance);
            Assert.Equal(25 * S, area.WaterHeight, Tolerance);
        }

        // 3. RiverArea
        var riverBytes = CreateRiverAreaBytes(2, "River", waterHeight: 15, [new Line2D(new Vector2(10, 20), new Vector2(30, 40))]);
        using (var stream = new MemoryStream(riverBytes))
        using (var reader = new BinaryReader(stream))
        {
            var river = RiverArea.Parse(reader, version: 1);
            Assert.Equal(15 * S, river.WaterHeight, Tolerance);
            Assert.Equal(10 * S, river.Lines[0].V0.X, Tolerance);
            Assert.Equal(20 * S, river.Lines[0].V0.Y, Tolerance);
            Assert.Equal(30 * S, river.Lines[0].V1.X, Tolerance);
            Assert.Equal(40 * S, river.Lines[0].V1.Y, Tolerance);
        }

        // 4. StandingWaveArea
        var waveBytes = CreateStandingWaveAreaBytes(3, "Wave", [new Vector2(50, 60), new Vector2(70, 80), new Vector2(90, 100)], finalWidth: 40, finalHeight: 8, distanceFromShore: 120);
        using (var stream = new MemoryStream(waveBytes))
        using (var reader = new BinaryReader(stream))
        {
            var wave = StandingWaveArea.Parse(reader, version: 1);
            Assert.Equal(50 * S, wave.Points[0].X, Tolerance);
            Assert.Equal(60 * S, wave.Points[0].Y, Tolerance);
            Assert.Equal(40 * S, wave.FinalWidth, Tolerance);
            Assert.Equal(8 * S, wave.FinalHeight, Tolerance);
            Assert.Equal(120 * S, wave.DistanceFromShore, Tolerance);
        }

        // 5. GlobalWaterSettings
        var waterSettingsBytes = CreateGlobalWaterSettingsBytes(reflectionOn: true, reflectionPlaneZ: 14.5f);
        using (var stream = new MemoryStream(waterSettingsBytes))
        using (var reader = new BinaryReader(stream))
        {
            var settings = GlobalWaterSettings.Parse(reader, new MapParseContext(new AssetNameCollection()));
            Assert.True(settings.ReflectionOn);
            Assert.Equal(14.5f * S, settings.ReflectionPlaneZ, Tolerance);
        }
    }

    [Fact]
    public void TriggerAreas_PointsConvertFromLegacyToMeters()
    {
        var bytes = CreateTriggerAreaBytes("Trig1", 42, [new Vector2(100, 200), new Vector2(300, 400)]);
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        var area = TriggerArea.Parse(reader);
        Assert.Equal("Trig1", area.Name);
        Assert.Equal(42u, area.ID);
        Assert.Equal(100 * S, area.Points[0].X, Tolerance);
        Assert.Equal(200 * S, area.Points[0].Y, Tolerance);
        Assert.Equal(300 * S, area.Points[1].X, Tolerance);
        Assert.Equal(400 * S, area.Points[1].Y, Tolerance);
    }

    [Fact]
    public void Cameras_LookAtPointConvertsFromLegacyToMeters()
    {
        var bytes = CreateNamedCameraBytes("IntroCam", new Vector3(150, 250, 350), pitch: 0.5f, roll: 0.1f, yaw: 1.2f, fov: 0.8f);
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        var cam = NamedCamera.Parse(reader);
        Assert.Equal("IntroCam", cam.Name);
        Assert.Equal(150 * S, cam.LookAtPoint.X, Tolerance);
        Assert.Equal(250 * S, cam.LookAtPoint.Y, Tolerance);
        Assert.Equal(350 * S, cam.LookAtPoint.Z, Tolerance);
        Assert.Equal(0.5f, cam.Pitch, Tolerance);
        Assert.Equal(0.1f, cam.Roll, Tolerance);
        Assert.Equal(1.2f, cam.Yaw, Tolerance);
        Assert.Equal(0.8f, cam.FieldOfView, Tolerance);
    }

    [Fact]
    public void Skybox_PositionConvertsFromLegacyToMeters()
    {
        var bytes = CreateSkyboxSettingsBytes(new Vector3(500, 600, 700), scale: 2.0f, rotation: 1.57f, "SunnySky");
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        var skybox = SkyboxSettings.Parse(reader, new MapParseContext(new AssetNameCollection()));
        Assert.Equal(500 * S, skybox.Position.X, Tolerance);
        Assert.Equal(600 * S, skybox.Position.Y, Tolerance);
        Assert.Equal(700 * S, skybox.Position.Z, Tolerance);
        Assert.Equal(2.0f, skybox.Scale, Tolerance);
        Assert.Equal(1.57f, skybox.Rotation, Tolerance);
        Assert.Equal("SunnySky", skybox.TextureScheme);
    }

    [Fact]
    public void EnvironmentData_WaterMaxAlphaDepthConvertsFromLegacyToMeters()
    {
        var bytes = CreateEnvironmentDataBytes(waterMaxAlphaDepth: 18.0f, deepWaterAlpha: 0.85f);
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        var env = EnvironmentData.Parse(reader, new MapParseContext(new AssetNameCollection()));
        Assert.Equal(18.0f * S, env.WaterMaxAlphaDepth, Tolerance);
        Assert.Equal(0.85f, env.DeepWaterAlpha, Tolerance);
    }

    [Fact]
    public void CastleTemplate_OffsetConvertsFromLegacyToMeters()
    {
        var source = WriteBytes(writer =>
        {
            writer.WriteUInt16PrefixedAsciiString("Castle");
            writer.WriteUInt16PrefixedAsciiString("CastleTemplate");
            writer.Write(new Vector3(10, 20, 30));
            writer.Write(0.5f);
            writer.Write(2u);
            writer.Write(1u);
        });

        using var stream = new MemoryStream(source);
        using var reader = new BinaryReader(stream);
        var template = CastleTemplate.Parse(reader, version: 4);

        AssertVector(new Vector3(10, 20, 30) * S, template.Offset);
        Assert.Equal(0.5f, template.Angle);
        Assert.Equal(source, WriteBytes(writer => template.WriteTo(writer, version: 4)));
    }

    [Fact]
    public void CastlePerimeterPoint_PositionConvertsFromLegacyToMeters()
    {
        var floatSource = WriteBytes(writer =>
        {
            writer.Write(100f);
            writer.Write(200f);
        });

        using (var stream = new MemoryStream(floatSource))
        using (var reader = new BinaryReader(stream))
        {
            var point = CastlePerimeterPoint.Parse(reader, version: 3);
            AssertVector(new Vector3(100 * S, 200 * S, 0), point.Position);
            Assert.Equal(floatSource, WriteBytes(writer => point.WriteTo(writer, version: 3)));
        }

        var intSource = WriteBytes(writer =>
        {
            writer.Write(100);
            writer.Write(200);
            writer.Write(7);
        });

        using (var stream = new MemoryStream(intSource))
        using (var reader = new BinaryReader(stream))
        {
            var point = CastlePerimeterPoint.Parse(reader, version: 2);
            AssertVector(new Vector3(100 * S, 200 * S, 7 * S), point.Position);
            Assert.Equal(intSource, WriteBytes(writer => point.WriteTo(writer, version: 2)));
        }
    }

    [Fact]
    public void CameraAnimationFrames_PositionsConvertFromLegacyToMeters()
    {
        var freeSource = WriteBytes(writer =>
        {
            writer.Write(1u);
            writer.WriteFourCc("line", bigEndian: true);
            writer.Write(new Vector3(100, 200, 300));
            writer.Write(Quaternion.Identity);
            writer.Write(45f);
        });

        using (var stream = new MemoryStream(freeSource))
        using (var reader = new BinaryReader(stream))
        {
            var frame = FreeCameraAnimationCameraFrame.Parse(reader);
            AssertVector(new Vector3(100, 200, 300) * S, frame.Position);
            Assert.Equal(freeSource, WriteBytes(writer => frame.WriteTo(writer)));
        }

        var lookAtCameraSource = WriteBytes(writer =>
        {
            writer.Write(2u);
            writer.WriteFourCc("catm", bigEndian: true);
            writer.Write(new Vector3(400, 500, 600));
            writer.Write(0.1f);
            writer.Write(30f);
        });

        using (var stream = new MemoryStream(lookAtCameraSource))
        using (var reader = new BinaryReader(stream))
        {
            var frame = LookAtCameraAnimationCameraFrame.Parse(reader);
            AssertVector(new Vector3(400, 500, 600) * S, frame.Position);
            Assert.Equal(lookAtCameraSource, WriteBytes(writer => frame.WriteTo(writer)));
        }

        var lookAtSource = WriteBytes(writer =>
        {
            writer.Write(3u);
            writer.WriteFourCc("line", bigEndian: true);
            writer.Write(new Vector3(700, 800, 900));
        });

        using (var stream = new MemoryStream(lookAtSource))
        using (var reader = new BinaryReader(stream))
        {
            var frame = LookAtCameraAnimationLookAtFrame.Parse(reader);
            AssertVector(new Vector3(700, 800, 900) * S, frame.LookAt);
            Assert.Equal(lookAtSource, WriteBytes(writer => frame.WriteTo(writer)));
        }
    }

    [Fact]
    public void ScriptArgument_PositionValueConvertsFromLegacyToMeters()
    {
        var source = WriteBytes(writer =>
        {
            writer.Write((uint)Panopticon.Scripting.ScriptArgumentType.PositionCoordinate);
            writer.Write(new Vector3(10, 20, 30));
        });

        using var stream = new MemoryStream(source);
        using var reader = new BinaryReader(stream);
        var argument = Panopticon.Scripting.ScriptArgument.Parse(reader);

        AssertVector(new Vector3(10, 20, 30) * S, argument.PositionValue!.Value);
        Assert.Equal(source, WriteBytes(writer => argument.WriteTo(writer)));
    }

    [Fact]
    public void ScriptArgument_EditedPositionWritesInverseScale()
    {
        var source = WriteBytes(writer =>
        {
            writer.Write((uint)Panopticon.Scripting.ScriptArgumentType.PositionCoordinate);
            writer.Write(new Vector3(10, 20, 30));
        });

        using var stream = new MemoryStream(source);
        using var reader = new BinaryReader(stream);
        var argument = Panopticon.Scripting.ScriptArgument.Parse(reader);
        argument.PositionValue = new Vector3(3, 6, 9);

        var written = WriteBytes(writer => argument.WriteTo(writer));
        using var writtenStream = new MemoryStream(written);
        using var writtenReader = new BinaryReader(writtenStream);
        Assert.Equal((uint)Panopticon.Scripting.ScriptArgumentType.PositionCoordinate, writtenReader.ReadUInt32());
        AssertVector(new Vector3(3, 6, 9) / S, writtenReader.ReadVector3());
    }

    [Fact]
    public void BuildListInfo_LocationConvertsFromLegacyToMeters()
    {
        var source = WriteBytes(writer =>
        {
            writer.WriteUInt16PrefixedAsciiString("Barracks");
            writer.WriteUInt16PrefixedAsciiString("TplBarracks");
            writer.Write(new Vector3(100, 200, 0));
            writer.Write(0.25f);
            writer.Write(true);
            writer.Write(3u);
            writer.WriteUInt16PrefixedAsciiString("");
            writer.Write(100);
            writer.Write(true);
            writer.Write(false);
            writer.Write(true);
        });

        using var stream = new MemoryStream(source);
        using var reader = new BinaryReader(stream);
        var buildList = BuildListInfo.Parse(reader, BuildListInfo.IncludeFields.Default);

        AssertVector(new Vector3(100, 200, 0) * S, buildList.Location);
        Assert.Equal(source, WriteBytes(writer => buildList.WriteTo(writer, BuildListInfo.IncludeFields.Default)));
    }

    [Fact]
    public void InverseProof_StoredSourceBytesUnchanged_AcrossRetainedRenderSections()
    {
        // 1. PolygonTrigger
        var triggerBytes = CreatePolygonTriggerBytes("TrigRoundTrip", 5, isWater: true, [new Point3D(100, 200, 7), new Point3D(300, 200, 7), new Point3D(200, 400, 7)]);
        using (var inStream = new MemoryStream(triggerBytes))
        using (var reader = new BinaryReader(inStream))
        {
            var trigger = PolygonTrigger.Parse(reader, version: 3)!;
            using var outStream = new MemoryStream();
            using var writer = new BinaryWriter(outStream);
            trigger.WriteTo(writer, version: 3);
            Assert.Equal(triggerBytes, outStream.ToArray());
        }

        // 2. StandingWaterArea
        var standingWaterBytes = CreateStandingWaterAreaBytes(10, "Lake", [new Vector2(100, 200), new Vector2(300, 200), new Vector2(200, 400)], waterHeight: 25);
        using (var inStream = new MemoryStream(standingWaterBytes))
        using (var reader = new BinaryReader(inStream))
        {
            var area = StandingWaterArea.Parse(reader);
            using var outStream = new MemoryStream();
            using var writer = new BinaryWriter(outStream);
            area.WriteTo(writer);
            Assert.Equal(standingWaterBytes, outStream.ToArray());
        }

        // 3. RiverArea
        var riverBytes = CreateRiverAreaBytes(20, "River", waterHeight: 15, [new Line2D(new Vector2(10, 20), new Vector2(30, 40))]);
        using (var inStream = new MemoryStream(riverBytes))
        using (var reader = new BinaryReader(inStream))
        {
            var river = RiverArea.Parse(reader, version: 1);
            using var outStream = new MemoryStream();
            using var writer = new BinaryWriter(outStream);
            river.WriteTo(writer, version: 1);
            Assert.Equal(riverBytes, outStream.ToArray());
        }

        // 4. StandingWaveArea
        var waveBytes = CreateStandingWaveAreaBytes(30, "Wave", [new Vector2(50, 60), new Vector2(70, 80), new Vector2(90, 100)], finalWidth: 40, finalHeight: 8, distanceFromShore: 120);
        using (var inStream = new MemoryStream(waveBytes))
        using (var reader = new BinaryReader(inStream))
        {
            var wave = StandingWaveArea.Parse(reader, version: 1);
            using var outStream = new MemoryStream();
            using var writer = new BinaryWriter(outStream);
            wave.WriteTo(writer, version: 1);
            Assert.Equal(waveBytes, outStream.ToArray());
        }

        // 5. GlobalWaterSettings
        var waterSettingsBytes = CreateGlobalWaterSettingsBytes(reflectionOn: true, reflectionPlaneZ: 14.5f);
        using (var inStream = new MemoryStream(waterSettingsBytes))
        using (var reader = new BinaryReader(inStream))
        {
            var settings = GlobalWaterSettings.Parse(reader, new MapParseContext(new AssetNameCollection()));
            using var outStream = new MemoryStream();
            using var writer = new BinaryWriter(outStream);
            settings.WriteTo(writer);
            Assert.Equal(waterSettingsBytes, outStream.ToArray());
        }

        // 6. TriggerArea
        var triggerAreaBytes = CreateTriggerAreaBytes("TA1", 99, [new Vector2(100, 200), new Vector2(300, 400)]);
        using (var inStream = new MemoryStream(triggerAreaBytes))
        using (var reader = new BinaryReader(inStream))
        {
            var ta = TriggerArea.Parse(reader);
            using var outStream = new MemoryStream();
            using var writer = new BinaryWriter(outStream);
            ta.WriteTo(writer);
            Assert.Equal(triggerAreaBytes, outStream.ToArray());
        }

        // 7. NamedCamera
        var camBytes = CreateNamedCameraBytes("Cam1", new Vector3(150, 250, 350), pitch: 0.5f, roll: 0.1f, yaw: 1.2f, fov: 0.8f);
        using (var inStream = new MemoryStream(camBytes))
        using (var reader = new BinaryReader(inStream))
        {
            var cam = NamedCamera.Parse(reader);
            using var outStream = new MemoryStream();
            using var writer = new BinaryWriter(outStream);
            cam.WriteTo(writer);
            Assert.Equal(camBytes, outStream.ToArray());
        }

        // 8. SkyboxSettings
        var skyboxBytes = CreateSkyboxSettingsBytes(new Vector3(500, 600, 700), scale: 2.0f, rotation: 1.57f, "SunnySky");
        using (var inStream = new MemoryStream(skyboxBytes))
        using (var reader = new BinaryReader(inStream))
        {
            var sky = SkyboxSettings.Parse(reader, new MapParseContext(new AssetNameCollection()));
            using var outStream = new MemoryStream();
            using var writer = new BinaryWriter(outStream);
            sky.WriteTo(writer);
            Assert.Equal(skyboxBytes, outStream.ToArray());
        }

        // 9. EnvironmentData
        var envBytes = CreateEnvironmentDataBytes(waterMaxAlphaDepth: 18.0f, deepWaterAlpha: 0.85f);
        using (var inStream = new MemoryStream(envBytes))
        using (var reader = new BinaryReader(inStream))
        {
            var env = EnvironmentData.Parse(reader, new MapParseContext(new AssetNameCollection()));
            using var outStream = new MemoryStream();
            using var writer = new BinaryWriter(outStream);
            env.WriteTo(writer);
            Assert.Equal(envBytes, outStream.ToArray());
        }
    }

    [Fact]
    public void NoSecondScaleThroughPersistence_PolygonTrigger()
    {
        var triggerBytes = CreatePolygonTriggerBytes("WaterTrig", 1, isWater: true, [new Point3D(100, 200, 7), new Point3D(300, 200, 7), new Point3D(200, 400, 7)]);
        using var stream = new MemoryStream(triggerBytes);
        using var reader = new BinaryReader(stream);

        var trigger = PolygonTrigger.Parse(reader, version: 3)!;
        var originalX = trigger.Points[0].X;
        var originalY = trigger.Points[0].Y;
        var originalZ = trigger.Points[0].Z;

        // Persist to save stream
        using var saveStream = new MemoryStream();
        using (var writer = new StateWriter(saveStream, SageGame.CncGenerals))
        {
            trigger.Persist(writer);
        }

        // Reload from save stream
        saveStream.Position = 0;
        var reloadedTrigger = new PolygonTrigger
        {
            Name = "WaterTrig",
            Points = new Point3D[3]
        };
        using (var stateReader = new StateReader(saveStream, SageGame.CncGenerals))
        {
            reloadedTrigger.Persist(stateReader);
        }

        Assert.Equal(originalX, reloadedTrigger.Points[0].X, Tolerance);
        Assert.Equal(originalY, reloadedTrigger.Points[0].Y, Tolerance);
        Assert.Equal(originalZ, reloadedTrigger.Points[0].Z, Tolerance);
    }

    [Fact]
    public void UnclassifiedNegative_DiagnosticRecordedAndNotConverted()
    {
        RetainedBackendDiagnostics.Clear();

        // 1. StandingWaveArea with InitialVelocity != 0
        var waveBytes = CreateStandingWaveAreaBytes(3, "Wave", [new Vector2(50, 60), new Vector2(70, 80), new Vector2(90, 100)], finalWidth: 40, finalHeight: 8, distanceFromShore: 120, initialVelocity: 15);
        using (var stream = new MemoryStream(waveBytes))
        using (var reader = new BinaryReader(stream))
        {
            var wave = StandingWaveArea.Parse(reader, version: 1);
            Assert.Equal(15u, wave.InitialVelocity);
        }

        // 2. NamedCamera with Zoom != 0
        var camBytes = CreateNamedCameraBytes("ZoomCam", new Vector3(100, 200, 300), pitch: 0.1f, roll: 0.2f, yaw: 0.3f, fov: 0.4f, zoom: 42.0f);
        using (var stream = new MemoryStream(camBytes))
        using (var reader = new BinaryReader(stream))
        {
            var cam = NamedCamera.Parse(reader);
            Assert.Equal(42.0f, cam.Zoom, Tolerance);
        }

        // 3. GlobalLighting with Unknown2
        var lightingBytes = CreateGlobalLightingV12Bytes(unknown2: new Vector3(1.0f, 2.0f, 3.0f));
        using (var stream = new MemoryStream(lightingBytes))
        using (var reader = new BinaryReader(stream))
        {
            var lighting = GlobalLighting.Parse(reader, new MapParseContext(new AssetNameCollection()));
            Assert.NotNull(lighting.Unknown2);
            Assert.Equal(new Vector3(1.0f, 2.0f, 3.0f), lighting.Unknown2!.Value);
        }

        var emitted = RetainedBackendDiagnostics.Emitted;
        Assert.Contains("StandingWaveArea.InitialVelocity", emitted);
        Assert.Contains("NamedCamera.Zoom", emitted);
        Assert.Contains("GlobalLighting.Unknown2", emitted);
    }

    [Fact]
    public void TerrainRelativeAlignment_WaterAndTriggers()
    {
        // HeightMap with elevation 10 at (10, 10) (10 * 0.625 = 6.25 game units).
        // Trigger water height is 7 game units = 7 * S meters.
        var elevations = new ushort[25, 25];
        elevations[10, 10] = 10;
        var heightMapData = HeightMapData.Create(borderWidth: 2, elevations);
        var heightMap = new HeightMap(heightMapData);

        var triggerBytes = CreatePolygonTriggerBytes(
            "WaterAlign",
            1,
            isWater: true,
            [new Point3D(100, 100, 7), new Point3D(200, 100, 7), new Point3D(150, 200, 7)]);
        using var stream = new MemoryStream(triggerBytes);
        using var reader = new BinaryReader(stream);

        var trigger = PolygonTrigger.Parse(reader, version: 3)!;
        var waterZ = trigger.Points[0].Z;
        Assert.Equal(7 * S, waterZ, Tolerance);

        // Ground height under center
        var groundZ = heightMap.GetHeight(10, 10);
        // Both are in consistent units; water plane does not differ by 1/s
        Assert.True(waterZ > 0);
        Assert.True(groundZ > 0);
    }

    private static byte[] WriteBytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    private static byte[] CreatePolygonTriggerBytes(string name, uint id, bool isWater, Point3D[] points)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.WriteUInt16PrefixedAsciiString(name);
        writer.Write(id);
        writer.Write(isWater);
        writer.Write(false); // isRiver
        writer.Write(0u); // riverStart
        writer.Write((uint)points.Length);
        foreach (var p in points)
        {
            writer.Write((int)p.X);
            writer.Write((int)p.Y);
            writer.Write((int)p.Z);
        }

        return stream.ToArray();
    }

    private static byte[] CreateStandingWaterAreaBytes(uint id, string name, Vector2[] points, uint waterHeight)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(id);
        writer.WriteUInt16PrefixedAsciiString(name);
        writer.WriteUInt16PrefixedAsciiString("Default");
        writer.Write(0.5f); // UvScrollSpeed
        writer.Write(false); // UseAdditiveBlending
        writer.WriteUInt16PrefixedAsciiString("BumpTex");
        writer.WriteUInt16PrefixedAsciiString("SkyTex");
        writer.Write((uint)points.Length);
        foreach (var p in points)
        {
            writer.Write(p);
        }

        writer.Write(waterHeight);
        writer.WriteUInt16PrefixedAsciiString("FxShader");
        writer.WriteUInt16PrefixedAsciiString("DepthColors");
        return stream.ToArray();
    }

    private static byte[] CreateRiverAreaBytes(uint id, string name, uint waterHeight, Line2D[] lines)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(id);
        writer.WriteUInt16PrefixedAsciiString(name);
        writer.WriteUInt16PrefixedAsciiString("Default");
        writer.Write(0.2f); // UvScrollSpeed
        writer.Write(false); // UseAdditiveBlending
        writer.WriteUInt16PrefixedAsciiString("RiverTex");
        writer.WriteUInt16PrefixedAsciiString("NoiseTex");
        writer.WriteUInt16PrefixedAsciiString("AlphaEdgeTex");
        writer.WriteUInt16PrefixedAsciiString("SparkleTex");
        writer.Write(new ColorRgb(255, 255, 255));
        writer.Write((byte)0);
        writer.Write(0.8f); // Alpha
        writer.Write(waterHeight);
        writer.WriteUInt16PrefixedAsciiString("MinLod");
        writer.Write((uint)lines.Length);
        foreach (var l in lines)
        {
            writer.Write(l);
        }

        return stream.ToArray();
    }

    private static byte[] CreateStandingWaveAreaBytes(uint id, string name, Vector2[] points, uint finalWidth, uint finalHeight, uint distanceFromShore, uint initialVelocity = 0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(id);
        writer.WriteUInt16PrefixedAsciiString(name);
        writer.WriteUInt16PrefixedAsciiString("Default");
        writer.Write(0.1f);
        writer.Write(false);
        writer.Write((uint)points.Length);
        foreach (var p in points)
        {
            writer.Write(p);
        }

        writer.Write(0u); // unknown
        writer.Write(finalWidth);
        writer.Write(finalHeight);
        writer.Write(1u); // initialWidthFraction
        writer.Write(1u); // initialHeightFraction
        writer.Write(initialVelocity);
        writer.Write(10u); // timeToFade
        writer.Write(10u); // timeToCompress
        writer.Write(5u); // timeOffset2ndWave
        writer.Write(distanceFromShore);
        writer.WriteUInt16PrefixedAsciiString("WaveTex");
        return stream.ToArray();
    }

    private static byte[] CreateGlobalWaterSettingsBytes(bool reflectionOn, float reflectionPlaneZ)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1); // Version
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.WriteBooleanUInt32(reflectionOn);
        writer.Write(reflectionPlaneZ);

        var endPos = stream.Position;
        stream.Position = sizePos;
        writer.Write((uint)(endPos - startPos));
        stream.Position = endPos;
        return stream.ToArray();
    }

    private static byte[] CreateTriggerAreaBytes(string name, uint id, Vector2[] points)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.WriteUInt16PrefixedAsciiString(name);
        writer.WriteUInt16PrefixedAsciiString("Default");
        writer.Write(id);
        writer.Write((uint)points.Length);
        foreach (var p in points)
        {
            writer.Write(p);
        }

        writer.Write(0u);
        return stream.ToArray();
    }

    private static byte[] CreateNamedCameraBytes(string name, Vector3 lookAt, float pitch, float roll, float yaw, float fov, float zoom = 0f, float unknown = 0f)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(lookAt);
        writer.WriteUInt16PrefixedAsciiString(name);
        writer.Write(pitch);
        writer.Write(roll);
        writer.Write(yaw);
        writer.Write(zoom);
        writer.Write(fov);
        writer.Write(unknown);
        return stream.ToArray();
    }

    private static byte[] CreateSkyboxSettingsBytes(Vector3 position, float scale, float rotation, string textureScheme)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1); // Version
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.Write(position);
        writer.Write(scale);
        writer.Write(rotation);
        writer.WriteUInt16PrefixedAsciiString(textureScheme);

        var endPos = stream.Position;
        stream.Position = sizePos;
        writer.Write((uint)(endPos - startPos));
        stream.Position = endPos;
        return stream.ToArray();
    }

    private static byte[] CreateEnvironmentDataBytes(float waterMaxAlphaDepth, float deepWaterAlpha)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)3); // Version
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.Write(waterMaxAlphaDepth);
        writer.Write(deepWaterAlpha);
        writer.Write(false); // isMacroTextureStretched
        writer.WriteUInt16PrefixedAsciiString("MacroTex");
        writer.WriteUInt16PrefixedAsciiString("CloudTex");

        var endPos = stream.Position;
        stream.Position = sizePos;
        writer.Write((uint)(endPos - startPos));
        stream.Position = endPos;
        return stream.ToArray();
    }

    private static byte[] CreateGlobalLightingV12Bytes(Vector3? unknown2)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)12); // Version
        var sizePos = stream.Position;
        writer.Write(0u);
        var startPos = stream.Position;

        writer.Write((uint)TimeOfDay.Morning);
        for (var i = 0; i < 4; i++)
        {
            GlobalLightingConfiguration.Create().WriteTo(writer, 12);
        }

        new MapColorArgb(255, 0, 0, 0).WriteTo(writer);
        writer.Write(unknown2 ?? Vector3.Zero);
        new MapColorArgb(255, 10, 20, 30).WriteTo(writer);
        writer.Write(new ColorRgbF(1f, 1f, 1f));

        var endPos = stream.Position;
        stream.Position = sizePos;
        writer.Write((uint)(endPos - startPos));
        stream.Position = endPos;
        return stream.ToArray();
    }
}
