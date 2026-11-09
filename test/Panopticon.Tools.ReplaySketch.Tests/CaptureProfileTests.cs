using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class CaptureProfileTests
{
    [Fact]
    public void ScorchScene_RoundTripsEveryScorchType()
    {
        var profile = CaptureProfileLoader.LoadEmbeddedCapture("vr-terrain-scorch.json");
        var loaded = RoundTrip(profile);
        Assert.Equal(profile.Scorches.Count, loaded.Scorches.Count);
        for (var index = 0; index < profile.Scorches.Count; index++)
        {
            Assert.Equal(profile.Scorches[index].Type, loaded.Scorches[index].Type);
            Assert.Equal(profile.Scorches[index].Radius, loaded.Scorches[index].Radius);
            Assert.Equal(profile.Scorches[index].X, loaded.Scorches[index].X);
            Assert.Equal(profile.Scorches[index].Y, loaded.Scorches[index].Y);
        }

        Assert.Empty(CaptureProfileValidator.Validate(loaded));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Scorches_RejectInvalidRadii(float radius)
    {
        var profile = CaptureProfileLoader.LoadEmbeddedCapture("vr-terrain-scorch.json");
        profile.Scorches[0].Radius = radius;
        Assert.Contains(CaptureProfileValidator.Validate(profile), error => error.StartsWith("Scorches need"));
    }

    [Fact]
    public void Scorches_RejectInvalidCoordinatesAndTypes()
    {
        var profile = CaptureProfileLoader.LoadEmbeddedCapture("vr-terrain-scorch.json");
        profile.Scorches[0].X = float.NaN;
        profile.Scorches[1].Type = (Panopticon.FX.TerrainScorchType)10;
        Assert.Equal(2, CaptureProfileValidator.Validate(profile).Count);
    }

    private static readonly JsonSerializerOptions RoundTripOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static CaptureProfile RoundTrip(CaptureProfile profile)
    {
        var path = Path.Combine(Path.GetTempPath(), $"capture-profile-test-{System.Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(profile, RoundTripOptions));
            return CaptureProfileLoader.LoadCaptureFromFile(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Profile_WithAllCommandAndExpectationTypes_RoundTrips()
    {
        var profile = new CaptureProfile
        {
            Name = "round-trip",
            DurationFrames = 200,
            SettleFrames = 5,
            CaptureStride = 4,
            Players = [new CapturePlayerSlot { Id = "p1", Side = "FactionGLA", Name = "GLA", StartingMoney = 500 }],
            Actors =
            [
                new CaptureActor { Id = "dozer", Definition = "AmericaVehicleDozer", Owner = "p1", X = 100, Y = 200, FacingDegrees = 90 },
                new CaptureActor { Id = "car", Definition = "CarAsian1", Owner = CaptureProfile.CivilianOwnerId, X = 300, Y = 200 },
            ],
            Camera = new CaptureCamera
            {
                Mode = CaptureCameraMode.AutoFit,
                SubjectActorIds = ["dozer"],
                FramingPoints = [new CapturePoint { X = 400, Y = 200, Z = 30 }],
                DesiredSpanFraction = 0.5f,
            },
            Commands =
            [
                new CaptureAttackCommand { Frame = 3, SourceActorId = "dozer", TargetActorId = "car" },
                new CaptureConstructCommand { Frame = 10, BuilderActorId = "dozer", BuildingDefinition = "AmericaBarracks", X = 400, Y = 200, ResultActorId = "barracks" },
                new CaptureApplyImpulseCommand { Frame = 10, ActorId = "car", DeltaVelocityX = 1.5f, DeltaVelocityZ = 0.25f },
            ],
            Expectations =
            [
                new CaptureProjectileObservedExpectation(),
                new CaptureActorDamagedExpectation { ActorId = "car" },
                new CaptureActorDestroyedExpectation { ActorId = "car" },
                new CaptureConstructionProgressedExpectation { ActorId = "barracks" },
                new CaptureConstructionCompletedExpectation { ActorId = "barracks" },
                new CaptureMinDisplacementExpectation { ActorId = "car", MinDisplacement = 25f },
                new CaptureProjectileOrientationExpectation
                {
                    Definition = "TestShell",
                    MinimumAlignmentDot = 0.99f,
                    MinimumSamples = 5,
                    RequireAscending = true,
                    RequireDescending = true,
                },
            ],
            StopWhenActorDestroyed = "car",
        };

        var loaded = RoundTrip(profile);

        Assert.Equal("round-trip", loaded.Name);
        Assert.Equal(4, loaded.CaptureStride);
        Assert.Single(loaded.Players);
        Assert.Equal(500u, loaded.Players[0].StartingMoney);
        Assert.Equal(2, loaded.Actors.Count);
        Assert.Equal(CaptureCameraMode.AutoFit, loaded.Camera.Mode);
        Assert.Single(loaded.Camera.FramingPoints);
        Assert.Equal(30f, loaded.Camera.FramingPoints[0].Z);

        Assert.Equal(3, loaded.Commands.Count);
        var attack = Assert.IsType<CaptureAttackCommand>(loaded.Commands[0]);
        Assert.Equal(3, attack.Frame);
        Assert.Equal("dozer", attack.SourceActorId);
        var construct = Assert.IsType<CaptureConstructCommand>(loaded.Commands[1]);
        Assert.Equal("barracks", construct.ResultActorId);
        var impulse = Assert.IsType<CaptureApplyImpulseCommand>(loaded.Commands[2]);
        Assert.Equal(1.5f, impulse.DeltaVelocityX);

        Assert.Equal(7, loaded.Expectations.Count);
        Assert.IsType<CaptureProjectileObservedExpectation>(loaded.Expectations[0]);
        var orientation = Assert.IsType<CaptureProjectileOrientationExpectation>(loaded.Expectations[^1]);
        Assert.Equal("TestShell", orientation.Definition);
        Assert.Equal(0.99f, orientation.MinimumAlignmentDot);
        Assert.True(orientation.RequireAscending);
        Assert.True(orientation.RequireDescending);
        Assert.Equal("car", loaded.StopWhenActorDestroyed);
    }

    [Fact]
    public void EmbeddedProfiles_LoadAndValidate()
    {
        var names = CaptureProfileLoader.EmbeddedCaptureProfileNames();
        Assert.Contains("capture-construction.json", names);
        Assert.Contains("capture-impulse.json", names);

        foreach (var name in names)
        {
            var profile = CaptureProfileLoader.LoadEmbeddedCapture(name);
            var errors = CaptureProfileValidator.Validate(profile);
            Assert.True(errors.Count == 0, $"{name}: {string.Join("; ", errors)}");
        }

        var legacy = CaptureProfileLoader.LoadEmbeddedCapture("capture-follow-move.json");
        var scale = (float)Panopticon.Content.Importing.Units.Materialization.UnitConversionPolicy.GeneralsFeetToMeters;
        Assert.Equal(470f * scale, legacy.Actors[0].X);
        Assert.Equal(CaptureProfile.MetricSchemaMarker, legacy.UnitSchema);
    }

    [Fact]
    public void Loader_MetricMarker_LoadsIdentity()
    {
        var path = WriteProfileFile("""
            {
              "UnitSchema": "metric-v1",
              "Name": "metric",
              "Actors": [ { "Id": "a", "Definition": "X", "Owner": "civilian", "X": 143.256, "Y": 213.36 } ],
              "Commands": [],
              "Expectations": []
            }
            """);

        try
        {
            var profile = CaptureProfileLoader.LoadCaptureFromFile(path);

            Assert.Equal(143.256f, profile.Actors[0].X);
            Assert.Equal(213.36f, profile.Actors[0].Y);
            Assert.Equal(CaptureProfile.MetricSchemaMarker, profile.UnitSchema);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loader_MarkerlessProfile_ConvertsLegacyUnitsOnce()
    {
        var path = WriteProfileFile("""
            {
              "Name": "legacy",
              "Actors": [
                { "Id": "mover", "Definition": "X", "Owner": "civilian", "X": 470, "Y": 700, "Z": 65 }
              ],
              "Scorches": [ { "X": 550, "Y": 600, "Radius": 40, "Type": "Scorch1" } ],
              "Camera": {
                "Mode": "Explicit",
                "LookAt": { "X": 540, "Y": 600 },
                "Height": 350,
                "FramingPoints": [ { "X": 600, "Y": 600 } ],
                "InitialCameraHeight": 120,
                "MinCameraHeight": 50,
                "MaxCameraHeight": 300
              },
              "Commands": [
                { "Type": "Move", "Frame": 0, "ActorId": "mover", "X": 570, "Y": 700 },
                { "Type": "ApplyImpulse", "Frame": 1, "ActorId": "mover", "DeltaVelocityX": 8.0 }
              ],
              "Expectations": [ { "Type": "MinDisplacement", "ActorId": "mover", "MinDisplacement": 40 } ]
            }
            """);

        try
        {
            var profile = CaptureProfileLoader.LoadCaptureFromFile(path);
            var scale = (float)Panopticon.Content.Importing.Units.Materialization.UnitConversionPolicy.GeneralsFeetToMeters;

            Assert.Equal(143.256f, profile.Actors[0].X, precision: 3);
            Assert.Equal(700f * scale, profile.Actors[0].Y);
            Assert.Equal(65f * scale, profile.Actors[0].Z!.Value);
            Assert.Equal(40f * scale, profile.Scorches[0].Radius);
            Assert.Equal(540f * scale, profile.Camera.LookAt!.X);
            Assert.Equal(350f * scale, profile.Camera.Height);
            Assert.Equal(600f * scale, profile.Camera.FramingPoints[0].Y);
            Assert.Equal(120f * scale, profile.Camera.InitialCameraHeight);
            Assert.Equal(50f * scale, profile.Camera.MinCameraHeight);
            Assert.Equal(300f * scale, profile.Camera.MaxCameraHeight);
            Assert.Equal(570f * scale, Assert.IsType<CaptureMoveCommand>(profile.Commands[0]).X);
            Assert.Equal(8f * scale, Assert.IsType<CaptureApplyImpulseCommand>(profile.Commands[1]).DeltaVelocityX);
            Assert.Equal(40f * scale, Assert.IsType<CaptureMinDisplacementExpectation>(profile.Expectations[0]).MinDisplacement);
            Assert.Equal(CaptureProfile.MetricSchemaMarker, profile.UnitSchema);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loader_ConvertedProfile_SerializesWithMarkerAndReloadsIdentically()
    {
        var path = WriteProfileFile("""
            {
              "Name": "legacy",
              "Actors": [ { "Id": "a", "Definition": "X", "Owner": "civilian", "X": 470, "Y": 700 } ],
              "Commands": [],
              "Expectations": []
            }
            """);

        try
        {
            var legacy = CaptureProfileLoader.LoadCaptureFromFile(path);
            var roundTripped = RoundTrip(legacy);

            Assert.Equal(legacy.Actors[0].X, roundTripped.Actors[0].X);
            Assert.Equal(CaptureProfile.MetricSchemaMarker, roundTripped.UnitSchema);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loader_UnknownUnitSchema_ThrowsTyped()
    {
        var path = WriteProfileFile("""
            {
              "UnitSchema": "furlongs-v1",
              "Name": "unknown",
              "Actors": [],
              "Commands": [],
              "Expectations": []
            }
            """);

        try
        {
            var exception = Assert.Throws<CaptureProfileLoader.UnsupportedUnitSchemaException>(
                () => CaptureProfileLoader.LoadCaptureFromFile(path));

            Assert.Contains("furlongs-v1", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteProfileFile(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"capture-profile-test-{System.Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Validator_CatchesStructuralProblems()
    {
        var profile = new CaptureProfile
        {
            Name = "broken",
            DurationFrames = 10,
            Players = [new CapturePlayerSlot { Id = "civilian", Side = "FactionAmerica" }],
            Actors =
            [
                new CaptureActor { Id = "a", Definition = "X", Owner = "nobody" },
                new CaptureActor { Id = "a", Definition = "X", Owner = CaptureProfile.CivilianOwnerId },
            ],
            Camera = new CaptureCamera { Mode = CaptureCameraMode.Explicit },
            Commands =
            [
                new CaptureAttackCommand { Frame = 99, SourceActorId = "a", TargetActorId = "missing" },
                new CaptureConstructCommand { Frame = 0, BuilderActorId = "a", BuildingDefinition = "", ResultActorId = "a" },
            ],
            Expectations = [new CaptureMinDisplacementExpectation { ActorId = "a", MinDisplacement = 0f }],
            StopWhenActorDestroyed = "ghost",
        };

        var errors = CaptureProfileValidator.Validate(profile);
        Assert.Contains(errors, e => e.Contains("reserved for the civilian owner"));
        Assert.Contains(errors, e => e.Contains("declared more than once"));
        Assert.Contains(errors, e => e.Contains("neither 'civilian' nor a declared player"));
        Assert.Contains(errors, e => e.Contains("outside 0..9"));
        Assert.Contains(errors, e => e.Contains("unknown target actor"));
        Assert.Contains(errors, e => e.Contains("needs a BuildingDefinition"));
        Assert.Contains(errors, e => e.Contains("collides with an existing"));
        Assert.Contains(errors, e => e.Contains("needs a LookAt point"));
        Assert.Contains(errors, e => e.Contains("positive Height"));
        Assert.Contains(errors, e => e.Contains("positive MinDisplacement"));
        Assert.Contains(errors, e => e.Contains("'ghost' is neither a declared nor a construct-result"));
    }

    [Fact]
    public void Schedule_ReturnsCommandsInDeclarationOrder()
    {
        var profile = new CaptureProfile
        {
            Commands =
            [
                new CaptureAttackCommand { Frame = 2, SourceActorId = "a", TargetActorId = "b" },
                new CaptureApplyImpulseCommand { Frame = 0, ActorId = "c" },
                new CaptureConstructCommand { Frame = 2, BuilderActorId = "a", BuildingDefinition = "B", ResultActorId = "r" },
            ],
        };
        var schedule = new CaptureSchedule(profile);

        Assert.Empty(schedule.DueAt(1));

        var frameZero = schedule.DueAt(0);
        Assert.Single(frameZero);
        Assert.IsType<CaptureApplyImpulseCommand>(frameZero[0]);

        var frameTwo = schedule.DueAt(2);
        Assert.Equal(2, frameTwo.Count);
        Assert.IsType<CaptureAttackCommand>(frameTwo[0]);
        Assert.IsType<CaptureConstructCommand>(frameTwo[1]);
    }

    [Fact]
    public void Validator_AcceptsFollowCamera()
    {
        var profile = ValidProfile();
        profile.Camera = new CaptureCamera
        {
            Mode = CaptureCameraMode.Follow,
            FollowActorId = "tank",
            SubjectActorIds = ["tank"],
        };

        var errors = CaptureProfileValidator.Validate(profile);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validator_FollowCameraNeedsAResolvableSubject()
    {
        var profile = ValidProfile();
        profile.Camera = new CaptureCamera
        {
            Mode = CaptureCameraMode.Follow,
            FollowActorId = "ghost",
            SubjectActorIds = ["tank"],
        };
        Assert.Contains(CaptureProfileValidator.Validate(profile),
            e => e.Contains("Follow camera actor 'ghost' is not a declared actor"));

        var noSubject = ValidProfile();
        noSubject.Camera = new CaptureCamera { Mode = CaptureCameraMode.Follow };
        Assert.Contains(CaptureProfileValidator.Validate(noSubject),
            e => e.Contains("Follow camera needs a FollowActorId, a FollowProjectileSourceActorId, or at least one SubjectActorIds entry"));
    }

    [Fact]
    public void Validator_AcceptsFollowProjectileCamera()
    {
        var profile = ValidProfile();
        profile.Camera = new CaptureCamera
        {
            Mode = CaptureCameraMode.Follow,
            FollowProjectileSourceActorId = "tank",
            SubjectActorIds = ["tank"],
        };

        Assert.Empty(CaptureProfileValidator.Validate(profile));
    }

    [Fact]
    public void Validator_FollowProjectileCameraRules()
    {
        var unknown = ValidProfile();
        unknown.Camera = new CaptureCamera
        {
            Mode = CaptureCameraMode.Follow,
            FollowProjectileSourceActorId = "ghost",
            SubjectActorIds = ["tank"],
        };
        Assert.Contains(CaptureProfileValidator.Validate(unknown),
            e => e.Contains("Follow camera projectile source 'ghost' is not a declared actor"));

        var both = ValidProfile();
        both.Camera = new CaptureCamera
        {
            Mode = CaptureCameraMode.Follow,
            FollowActorId = "tank",
            FollowProjectileSourceActorId = "tank",
            SubjectActorIds = ["tank"],
        };
        Assert.Contains(CaptureProfileValidator.Validate(both),
            e => e.Contains("cannot set both FollowActorId and FollowProjectileSourceActorId"));
    }

    [Fact]
    public void Camera_OrientationOverrides_RoundTrip()
    {
        var overrides = new CaptureProfile
        {
            Name = "orientation",
            Camera = new CaptureCamera
            {
                Mode = CaptureCameraMode.Explicit,
                LookAt = new CapturePoint { X = 1, Y = 2, Z = 3 },
                Height = 250,
                PitchDegrees = -30,
                AngleDegrees = 45,
            },
        };

        var loadedOverrides = RoundTrip(overrides);
        Assert.Equal(CaptureCameraMode.Explicit, loadedOverrides.Camera.Mode);
        Assert.Equal(-30, loadedOverrides.Camera.PitchDegrees);
        Assert.Equal(45, loadedOverrides.Camera.AngleDegrees);

        var defaults = new CaptureProfile
        {
            Name = "defaults",
            Camera = new CaptureCamera(),
        };

        var loadedDefaults = RoundTrip(defaults);
        Assert.Null(loadedDefaults.Camera.PitchDegrees);
        Assert.Null(loadedDefaults.Camera.AngleDegrees);
    }

    private static CaptureProfile ValidProfile() => new()
    {
        Name = "follow",
        DurationFrames = 10,
        MapPath = "maps/alpine assault/alpine assault.map",
        Actors = [new CaptureActor { Id = "tank", Definition = "X", Owner = CaptureProfile.CivilianOwnerId }],
        Camera = new CaptureCamera(),
    };
}
