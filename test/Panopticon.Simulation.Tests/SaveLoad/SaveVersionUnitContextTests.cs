using System;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data.Sav;
using Panopticon.Logic.Map;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.SaveLoad;

public sealed class SaveVersionUnitContextTests
{
    [Fact]
    public void LegacyV2Stream_PolygonTriggerAndBuildListInfo_ConvertsFeetToMeters()
    {
        var legacyTrigger = new PolygonTrigger
        {
            Name = "WaterArea",
            Points =
            [
                new Point3D(100f, 200f, 10f),
                new Point3D(500f, 200f, 10f),
                new Point3D(500f, 600f, 10f),
                new Point3D(100f, 600f, 10f),
            ],
        };
        _ = legacyTrigger.Bounds;
        _ = legacyTrigger.Radius;

        var legacyBuildList = new BuildListInfo
        {
            Location = new Vector3(100f, 200f, 50f),
            RallyPointOffset = new Vector2(10f, 20f),
            Angle = 1.57f,
            Health = 100,
        };

        byte[] legacyStreamBytes;
        using (var stream = new MemoryStream())
        {
            using (var writer = new StateWriter(stream, SageGame.CncGenerals))
            {
                writer.BeginObject();
                byte version2 = 2;
                writer.PersistByte(ref version2, "Version");
                var gameState = new GameState();
                writer.PersistEnum(ref gameState.GameType);
                writer.PersistAsciiString(ref gameState.MapPath);
                writer.PersistDateTime(ref gameState.Timestamp);
                writer.PersistUnicodeString(ref gameState.DisplayName);
                writer.PersistAsciiString(ref gameState.MapFileName);
                writer.PersistAsciiString(ref gameState.Side);
                writer.PersistUInt32(ref gameState.MissionIndex);
                writer.EndObject();

                writer.PersistObject(legacyTrigger);
                writer.PersistObject(legacyBuildList);
            }

            legacyStreamBytes = stream.ToArray();
        }

        using (var stream = new MemoryStream(legacyStreamBytes, writable: false))
        {
            using var reader = new StateReader(stream, SageGame.CncGenerals);

            var readGameState = new GameState();
            reader.PersistObject(readGameState);
            Assert.Equal(StreamUnitContext.LegacyFeet, reader.UnitContext);

            var readTrigger = new PolygonTrigger
            {
                Name = "WaterArea",
                Points = new Point3D[legacyTrigger.Points.Length],
            };
            reader.PersistObject(readTrigger);

            var readBuildList = new BuildListInfo();
            reader.PersistObject(readBuildList);

            const float tolerance = 1e-4f;
            const float s = (float)UnitConversionPolicy.GeneralsFeetToMeters;

            for (var i = 0; i < legacyTrigger.Points.Length; i++)
            {
                Assert.Equal(legacyTrigger.Points[i].X * s, readTrigger.Points[i].X, tolerance);
                Assert.Equal(legacyTrigger.Points[i].Y * s, readTrigger.Points[i].Y, tolerance);
                Assert.Equal(legacyTrigger.Points[i].Z * s, readTrigger.Points[i].Z, tolerance);
            }

            Assert.Equal(legacyTrigger.Radius * s, readTrigger.Radius, tolerance);
            Assert.Equal((int)Math.Round(legacyTrigger.Bounds.TopLeft.X * s), readTrigger.Bounds.TopLeft.X);
            Assert.Equal((int)Math.Round(legacyTrigger.Bounds.TopLeft.Y * s), readTrigger.Bounds.TopLeft.Y);
            Assert.Equal((int)Math.Round(legacyTrigger.Bounds.BottomRight.X * s), readTrigger.Bounds.BottomRight.X);
            Assert.Equal((int)Math.Round(legacyTrigger.Bounds.BottomRight.Y * s), readTrigger.Bounds.BottomRight.Y);

            Assert.Equal(legacyBuildList.Location.X * s, readBuildList.Location.X, tolerance);
            Assert.Equal(legacyBuildList.Location.Y * s, readBuildList.Location.Y, tolerance);
            Assert.Equal(legacyBuildList.Location.Z * s, readBuildList.Location.Z, tolerance);

            Assert.Equal(legacyBuildList.RallyPointOffset.X * s, readBuildList.RallyPointOffset.X, tolerance);
            Assert.Equal(legacyBuildList.RallyPointOffset.Y * s, readBuildList.RallyPointOffset.Y, tolerance);

            Assert.Equal(legacyBuildList.Angle, readBuildList.Angle);
            Assert.Equal(legacyBuildList.Health, readBuildList.Health);
        }
    }

    [Fact]
    public void V3Stream_WriteThenRead_RoundTripsWithoutScaling()
    {
        var metricTrigger = new PolygonTrigger
        {
            Name = "WaterArea",
            Points =
            [
                new Point3D(30.48f, 60.96f, 3.048f),
                new Point3D(152.4f, 60.96f, 3.048f),
                new Point3D(152.4f, 182.88f, 3.048f),
                new Point3D(30.48f, 182.88f, 3.048f),
            ],
        };
        _ = metricTrigger.Bounds;
        _ = metricTrigger.Radius;

        var metricBuildList = new BuildListInfo
        {
            Location = new Vector3(30.48f, 60.96f, 15.24f),
            RallyPointOffset = new Vector2(3.048f, 6.096f),
            Angle = 1.57f,
            Health = 100,
        };

        byte[] v3StreamBytes;
        using (var stream = new MemoryStream())
        {
            using (var writer = new StateWriter(stream, SageGame.CncGenerals))
            {
                var gameState = new GameState();
                writer.PersistObject(gameState);

                writer.PersistObject(metricTrigger);
                writer.PersistObject(metricBuildList);
            }

            v3StreamBytes = stream.ToArray();
        }

        using (var stream = new MemoryStream(v3StreamBytes, writable: false))
        {
            using var reader = new StateReader(stream, SageGame.CncGenerals);

            var readGameState = new GameState();
            reader.PersistObject(readGameState);
            Assert.Equal(StreamUnitContext.Metric, reader.UnitContext);

            var readTrigger = new PolygonTrigger
            {
                Name = "WaterArea",
                Points = new Point3D[metricTrigger.Points.Length],
            };
            reader.PersistObject(readTrigger);

            var readBuildList = new BuildListInfo();
            reader.PersistObject(readBuildList);

            for (var i = 0; i < metricTrigger.Points.Length; i++)
            {
                Assert.Equal(metricTrigger.Points[i].X, readTrigger.Points[i].X);
                Assert.Equal(metricTrigger.Points[i].Y, readTrigger.Points[i].Y);
                Assert.Equal(metricTrigger.Points[i].Z, readTrigger.Points[i].Z);
            }

            Assert.Equal(metricTrigger.Radius, readTrigger.Radius);
            Assert.Equal(metricTrigger.Bounds.TopLeft, readTrigger.Bounds.TopLeft);
            Assert.Equal(metricTrigger.Bounds.BottomRight, readTrigger.Bounds.BottomRight);

            Assert.Equal(metricBuildList.Location, readBuildList.Location);
            Assert.Equal(metricBuildList.RallyPointOffset, readBuildList.RallyPointOffset);
            Assert.Equal(metricBuildList.Angle, readBuildList.Angle);
            Assert.Equal(metricBuildList.Health, readBuildList.Health);
        }
    }

    [Fact]
    public void StreamVersionAbove3_ThrowsInvalidStateException()
    {
        byte[] invalidGameStateBytes;
        using (var stream = new MemoryStream())
        {
            using (var writer = new StateWriter(stream, SageGame.CncGenerals))
            {
                writer.BeginObject();
                byte version4 = 4;
                writer.PersistByte(ref version4, "Version");
                writer.EndObject();
            }

            invalidGameStateBytes = stream.ToArray();
        }

        using (var stream = new MemoryStream(invalidGameStateBytes, writable: false))
        {
            using var reader = new StateReader(stream, SageGame.CncGenerals);
            var readGameState = new GameState();
            Assert.Throws<InvalidStateException>(() => reader.PersistObject(readGameState));
        }

        byte[] invalidGameStateMapBytes;
        using (var stream = new MemoryStream())
        {
            using (var writer = new StateWriter(stream, SageGame.CncGenerals))
            {
                writer.BeginObject();
                byte version4 = 4;
                writer.PersistByte(ref version4, "Version");
                writer.EndObject();
            }

            invalidGameStateMapBytes = stream.ToArray();
        }

        using (var stream = new MemoryStream(invalidGameStateMapBytes, writable: false))
        {
            using var reader = new StateReader(stream, SageGame.CncGenerals);
            var readGameStateMap = new GameStateMap();
            Assert.Throws<InvalidStateException>(() => reader.PersistObject(readGameStateMap));
        }
    }
}
