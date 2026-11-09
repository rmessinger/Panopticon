using System;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data.Sav;
using Panopticon.Logic;
using Panopticon.Logic.Map;
using Panopticon.Logic.Object;
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

    [Fact]
    public void LegacyV2Stream_GameObjectTransformMatrix_ScalesOnlyTranslation()
    {
        var rotation = Matrix4x4.CreateRotationZ(0.785398f);
        var originalMatrix = new Matrix4x3(
            rotation.M11, rotation.M12, rotation.M13,
            rotation.M21, rotation.M22, rotation.M23,
            rotation.M31, rotation.M32, rotation.M33,
            100f, 200f, 50f);

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

                var matrixToWrite = originalMatrix;
                writer.PersistLengthMatrix4x3(ref matrixToWrite);
            }

            legacyStreamBytes = stream.ToArray();
        }

        using (var stream = new MemoryStream(legacyStreamBytes, writable: false))
        {
            using var reader = new StateReader(stream, SageGame.CncGenerals);
            var readGameState = new GameState();
            reader.PersistObject(readGameState);
            Assert.Equal(StreamUnitContext.LegacyFeet, reader.UnitContext);

            var readMatrix = Matrix4x3.Identity;
            reader.PersistLengthMatrix4x3(ref readMatrix);

            // Rotation elements must be untouched
            Assert.Equal(originalMatrix.M11, readMatrix.M11);
            Assert.Equal(originalMatrix.M12, readMatrix.M12);
            Assert.Equal(originalMatrix.M13, readMatrix.M13);
            Assert.Equal(originalMatrix.M21, readMatrix.M21);
            Assert.Equal(originalMatrix.M22, readMatrix.M22);
            Assert.Equal(originalMatrix.M23, readMatrix.M23);
            Assert.Equal(originalMatrix.M31, readMatrix.M31);
            Assert.Equal(originalMatrix.M32, readMatrix.M32);
            Assert.Equal(originalMatrix.M33, readMatrix.M33);

            // Translation elements must scale by GeneralsFeetToMeters
            const float s = (float)UnitConversionPolicy.GeneralsFeetToMeters;
            const float tolerance = 1e-4f;
            Assert.Equal(100f * s, readMatrix.M41, tolerance);
            Assert.Equal(200f * s, readMatrix.M42, tolerance);
            Assert.Equal(50f * s, readMatrix.M43, tolerance);
        }
    }

    [Fact]
    public void LegacyV2Stream_RepresentativeModuleSites_ConvertLengthsAndPreserveNonLengths()
    {
        var velocity = new Vector3(10f, 20f, 5f);
        var rallyPoint = new RallyPointManager();
        rallyPoint.SetRallyPoint(new Vector3(100f, 200f, 30f));
        var shroudReveal = new ShroudReveal
        {
            Position = new Vector3(100f, 200f, 10f),
            VisionRange = 50f,
            Unknown = 42,
        };
        var unitDirection = Vector3.Normalize(new Vector3(1f, 1f, 0f));
        var angularVelocity = 1.57f;

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

                var vel = velocity;
                writer.PersistLengthVector3(ref vel);
                writer.PersistObject(rallyPoint);
                writer.PersistObject(shroudReveal);

                var dir = unitDirection;
                writer.PersistVector3(ref dir);
                var angVel = angularVelocity;
                writer.PersistSingle(ref angVel);
            }

            legacyStreamBytes = stream.ToArray();
        }

        using (var stream = new MemoryStream(legacyStreamBytes, writable: false))
        {
            using var reader = new StateReader(stream, SageGame.CncGenerals);
            var readGameState = new GameState();
            reader.PersistObject(readGameState);

            var readVelocity = Vector3.Zero;
            reader.PersistLengthVector3(ref readVelocity);

            var readRallyPoint = new RallyPointManager();
            reader.PersistObject(readRallyPoint);

            var readShroudReveal = new ShroudReveal();
            reader.PersistObject(readShroudReveal);

            var readDir = Vector3.Zero;
            reader.PersistVector3(ref readDir);

            var readAngVel = 0f;
            reader.PersistSingle(ref readAngVel);

            const float s = (float)UnitConversionPolicy.GeneralsFeetToMeters;
            const float tolerance = 1e-4f;

            // Length sites must convert under v2
            Assert.Equal(velocity.X * s, readVelocity.X, tolerance);
            Assert.Equal(velocity.Y * s, readVelocity.Y, tolerance);
            Assert.Equal(velocity.Z * s, readVelocity.Z, tolerance);

            Assert.True(readRallyPoint.RallyPoint.HasValue);
            Assert.Equal(100f * s, readRallyPoint.RallyPoint.Value.X, tolerance);
            Assert.Equal(200f * s, readRallyPoint.RallyPoint.Value.Y, tolerance);
            Assert.Equal(30f * s, readRallyPoint.RallyPoint.Value.Z, tolerance);

            Assert.Equal(shroudReveal.Position.X * s, readShroudReveal.Position.X, tolerance);
            Assert.Equal(shroudReveal.Position.Y * s, readShroudReveal.Position.Y, tolerance);
            Assert.Equal(shroudReveal.Position.Z * s, readShroudReveal.Position.Z, tolerance);
            Assert.Equal(shroudReveal.VisionRange * s, readShroudReveal.VisionRange, tolerance);

            // Non-length sites must NOT convert under v2
            Assert.Equal(shroudReveal.Unknown, readShroudReveal.Unknown);
            Assert.Equal(unitDirection.X, readDir.X, tolerance);
            Assert.Equal(unitDirection.Y, readDir.Y, tolerance);
            Assert.Equal(unitDirection.Z, readDir.Z, tolerance);
            Assert.Equal(angularVelocity, readAngVel, tolerance);
        }
    }

    [Fact]
    public void V3Stream_RepresentativeModuleSites_RoundTripIdentity()
    {
        var velocity = new Vector3(3.048f, 6.096f, 1.524f);
        var rallyPoint = new RallyPointManager();
        rallyPoint.SetRallyPoint(new Vector3(30.48f, 60.96f, 9.144f));
        var shroudReveal = new ShroudReveal
        {
            Position = new Vector3(30.48f, 60.96f, 3.048f),
            VisionRange = 15.24f,
            Unknown = 42,
        };
        var unitDirection = Vector3.Normalize(new Vector3(1f, 1f, 0f));

        byte[] v3StreamBytes;
        using (var stream = new MemoryStream())
        {
            using (var writer = new StateWriter(stream, SageGame.CncGenerals))
            {
                var gameState = new GameState();
                writer.PersistObject(gameState);

                var vel = velocity;
                writer.PersistLengthVector3(ref vel);
                writer.PersistObject(rallyPoint);
                writer.PersistObject(shroudReveal);
                var dir = unitDirection;
                writer.PersistVector3(ref dir);
            }

            v3StreamBytes = stream.ToArray();
        }

        using (var stream = new MemoryStream(v3StreamBytes, writable: false))
        {
            using var reader = new StateReader(stream, SageGame.CncGenerals);
            var readGameState = new GameState();
            reader.PersistObject(readGameState);
            Assert.Equal(StreamUnitContext.Metric, reader.UnitContext);

            var readVelocity = Vector3.Zero;
            reader.PersistLengthVector3(ref readVelocity);

            var readRallyPoint = new RallyPointManager();
            reader.PersistObject(readRallyPoint);

            var readShroudReveal = new ShroudReveal();
            reader.PersistObject(readShroudReveal);

            var readDir = Vector3.Zero;
            reader.PersistVector3(ref readDir);

            // Values must remain identical under v3
            Assert.Equal(velocity, readVelocity);
            Assert.Equal(rallyPoint.RallyPoint, readRallyPoint.RallyPoint);
            Assert.Equal(shroudReveal.Position, readShroudReveal.Position);
            Assert.Equal(shroudReveal.VisionRange, readShroudReveal.VisionRange);
            Assert.Equal(shroudReveal.Unknown, readShroudReveal.Unknown);
            Assert.Equal(unitDirection, readDir);
        }
    }
}
