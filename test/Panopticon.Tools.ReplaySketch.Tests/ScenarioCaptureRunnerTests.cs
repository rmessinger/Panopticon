using System.IO;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.TestHarness;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Serial, real-install runs of the generic capture profiles: construction must progress and
/// complete, and an impulse must move the subject. Skipped (not failed) when no Generals
/// installation or no graphics backend is available on the host.
/// </summary>
public class ScenarioCaptureRunnerTests
{
    [Fact]
    public async System.Threading.Tasks.Task ImpulseProfile_MovesSubject()
    {
        var outcome = await RunProfileAsync("capture-impulse.json", "impulse-test");

        if (outcome == null)
        {
            return;
        }

        Assert.Equal(0, outcome.ExitCode);
        Assert.True(outcome.CapturedFrames > 0);
        Assert.Contains(outcome.ExpectationResults, e => e.Passed && e.Expectation.Contains("moves at least"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ConstructionProfile_ProgressesAndCompletes()
    {
        var outcome = await RunProfileAsync("capture-construction.json", "construction-test");

        if (outcome == null)
        {
            return;
        }

        Assert.Equal(0, outcome.ExitCode);
        Assert.Contains(outcome.CommandResults, c => c.Status == "Dispatched" && c.Command.Contains("Construct"));
        Assert.Contains(outcome.ExpectationResults, e => e.Passed && e.Expectation.Contains("progresses"));
        Assert.Contains(outcome.ExpectationResults, e => e.Passed && e.Expectation.Contains("completes"));
    }

    private static async System.Threading.Tasks.Task<CaptureRunResult?> RunProfileAsync(string embeddedName, string directorySuffix)
    {
        var installationService = new GeneralsInstallationService();
        if (!installationService.TryGetInstallation(out var installation) || installation is null)
        {
            // No install on this host; not a harness failure.
            return null;
        }

        var profile = CaptureProfileLoader.LoadEmbeddedCapture(embeddedName);
        profile.DurationFrames = Math.Min(profile.DurationFrames, 550);
        var output = Path.Combine(Path.GetTempPath(), $"panopticon_{directorySuffix}_{Guid.NewGuid():N}");
        profile.OutputDirectory = output;

        try
        {
            // The runner drives rendering headlessly; keep it off the test thread so a stuck
            // native graphics call surfaces as a test timeout rather than a hang.
            return await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    return ScenarioCaptureRunner.Run(
                        installation,
                        profile,
                        new CaptureRunSettings(null, FullHud: false, FallbackOutputDirectory: null),
                        new NopReporter());
                }
                catch (TypeInitializationException ex) when (ex.InnerException is FileNotFoundException)
                {
                    // Missing native graphics libraries: host limitation, skip.
                    return null;
                }
            });
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    private sealed class NopReporter : ITestHarnessReporter
    {
        public void Info(string message)
        {
        }

        public void Error(string message)
        {
        }

        public void ActionDispatched(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void ActionCompleted(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void MatchOutcomeDecided(Logic.OutcomeStatus status, Logic.OutcomeReason reason, string? winnerName, uint frame)
        {
        }
    }
}
