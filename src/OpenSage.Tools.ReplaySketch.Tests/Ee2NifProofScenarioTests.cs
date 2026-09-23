using System;
using System.Collections.Generic;
using System.IO;
using OpenSage.Tools.TestHarness;
using Xunit;
using ActionType = OpenSage.Tools.ReplaySketch.Model.ActionType;

namespace OpenSage.Tools.ReplaySketch.Tests;

public sealed class Ee2NifProofScenarioTests
{
    private sealed class StringReporter : ITestHarnessReporter
    {
        public System.Text.StringBuilder Errors { get; } = new();

        public void Info(string message)
        {
        }

        public void Error(string message)
        {
            Errors.AppendLine(message);
        }

        public void ActionDispatched(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void ActionCompleted(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void MatchOutcomeDecided(
            OpenSage.Logic.OutcomeStatus status,
            OpenSage.Logic.OutcomeReason reason,
            string? winnerName,
            uint frame)
        {
        }
    }

    [Fact]
    public void CapturePlanTargetsBothProofModels()
    {
        Assert.Equal(2, Ee2NifProofScenario.CapturePlan.Length);
        Assert.Equal("nwg1_galley", Ee2NifProofScenario.CapturePlan[0].Key);
        Assert.Equal("ab11_TwinEngineBomber", Ee2NifProofScenario.CapturePlan[1].Key);
        Assert.All(
            Ee2NifProofScenario.CapturePlan,
            entry => Assert.False(string.IsNullOrWhiteSpace(entry.Check)));
    }

    [Fact]
    public void AnimatedCapturePlanCoversDeepDiveSectionEight()
    {
        var plan = Ee2NifProofScenario.AnimatedCapturePlan;
        Assert.Equal(8, plan.Length);

        var expected = new[]
        {
            ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 0f),
            ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 0.767f),
            ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 1.433f),
            ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_idle.kf", 0f),
            ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf", 0f),
            ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf", 1f / 12f),
            ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf", 0f),
            ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf", 1.333f),
        };

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Item1, plan[i].ModelKey);
            Assert.Equal(expected[i].Item2, plan[i].KfPath);
            Assert.Equal(expected[i].Item3, plan[i].TimeSeconds);
        }

        Assert.All(plan, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Check)));
    }

    [Fact]
    public void AnimatedCaptureFileNameUsesKfStemAndPaddedMilliseconds()
    {
        Assert.Equal(
            "ee2-nwg1_galley_walk_0000.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName("Graphics/Vehicles/nwg1_galley_walk.kf", 0f));
        Assert.Equal(
            "ee2-nwg1_galley_walk_0767.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName("Graphics/Vehicles/nwg1_galley_walk.kf", 0.767f));
        Assert.Equal(
            "ee2-nwg1_galley_walk_1433.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName("Graphics/Vehicles/nwg1_galley_walk.kf", 1.433f));
        Assert.Equal(
            "ee2-ab11_TwinEngineBomber_walk_0083.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName(
                "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf", 1f / 12f));
        Assert.Equal(
            "ee2-ab11_TwinEngineBomber_takeoff_1333.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName(
                "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf", 1.333f));
    }

    [Fact]
    public void ValidationGuideListsAnimatedCapturesAndDeterminismNote()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ee2-proof-guide-test");
        Directory.CreateDirectory(directory);

        try
        {
            var captures = new List<(string FileName, string Check)>
            {
                ("ee2-nwg1_galley.png", "static hull"),
                ("ee2-nwg1_galley_walk_0767.png", "mid-stroke oars"),
                ("ee2-ab11_TwinEngineBomber_walk_0083.png", "quarter-turn props"),
            };

            Ee2NifProofScenario.WriteValidationGuide(directory, captures);

            var guide = File.ReadAllText(Path.Combine(directory, "validation_guide.md"));
            Assert.Contains("ee2-nwg1_galley_walk_0767.png", guide);
            Assert.Contains("ee2-ab11_TwinEngineBomber_walk_0083.png", guide);
            Assert.Contains("NifAnimationImporter", guide);
            Assert.Contains("Deterministic repeat", guide);
            Assert.Contains("synchronously", guide);
        }

        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingEe2InstallIsReportedDirectly()
    {
        var previous = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH");
        Environment.SetEnvironmentVariable("EMPIRE_EARTH_II_PATH", null);
        try
        {
            var reporter = new StringReporter();
            var exitCode = Ee2NifProofScenario.Run(null!, new TestHarnessConfig(), reporter);
            Assert.Equal(1, exitCode);
            Assert.Contains("EMPIRE_EARTH_II_PATH", reporter.Errors.ToString());
        }

        finally
        {
            Environment.SetEnvironmentVariable("EMPIRE_EARTH_II_PATH", previous);
        }
    }

    [Fact]
    public void MissingGraphicsArchiveIsReportedDirectly()
    {
        var previous = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH");
        var tempRoot = Path.Combine(Path.GetTempPath(), "ee2-proof-missing-test");
        Directory.CreateDirectory(tempRoot);
        Environment.SetEnvironmentVariable("EMPIRE_EARTH_II_PATH", tempRoot);
        try
        {
            var reporter = new StringReporter();
            var exitCode = Ee2NifProofScenario.Run(null!, new TestHarnessConfig(), reporter);
            Assert.Equal(1, exitCode);
            Assert.Contains("graphics.zip", reporter.Errors.ToString());
        }

        finally
        {
            Environment.SetEnvironmentVariable("EMPIRE_EARTH_II_PATH", previous);
            Directory.Delete(tempRoot, recursive: false);
        }
    }

    [Fact]
    public void ScenarioIsDispatchedByBuiltInScenario()
    {
        Assert.True(Enum.IsDefined<BuiltInScenario>(BuiltInScenario.Ee2NifProof));
    }
}
