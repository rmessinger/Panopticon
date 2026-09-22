using System;
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
