using System;
using System.IO;
using System.Linq;
using Panopticon.Recording;
using Panopticon.Tests.Data;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Recording;

/// <summary>
/// WI-A5-09 against the real install: one harness run emits a complete bundle (manifest,
/// state artifact, structured events) that validates on load and survives relocation.
/// </summary>
[Collection("SerialGameTests")]
public sealed class RunBundleTests(ITestOutputHelper output) : MockedGameTest
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "a5-runbundle-" + Guid.NewGuid().ToString("N"));

    private void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void RunBundle_CompleteHarnessRun_WritesManifestStateAndEvents()
    {
        try
        {
            var bundleDirectory = Path.Combine(_root, "bundle");
            var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA();
            scenario.Seed = 5150;
            scenario.StopConditions.Add(new FrameCapStopCondition(600));
            scenario.Recording = new ScenarioRecordingOptions
            {
                Mode = RecordingMode.Full,
                SampleInterval = 15,
                SnapshotInterval = 100,
            };

            HarnessRunner.RunAndAssert(
                scenario,
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                Path.Combine(_root, "temp"),
                onRunCompleted: null,
                bundleDirectory: bundleDirectory);

            Assert.True(File.Exists(Path.Combine(bundleDirectory, RunBundle.ManifestFileName)));
            Assert.True(File.Exists(Path.Combine(bundleDirectory, RunBundle.StateFileName)));
            Assert.True(File.Exists(Path.Combine(bundleDirectory, RunBundle.EventsFileName)));

            var contents = RunBundle.Load(bundleDirectory);
            Assert.Equal(5150u, contents.Manifest.Seed);
            Assert.Contains(0u, contents.Manifest.SnapshotFrames);

            output.WriteLine($"events: {contents.Events.Count}");
            Assert.NotEmpty(contents.Events);
            Assert.All(contents.Events, e => Assert.False(string.IsNullOrEmpty(e.Category)));
            Assert.All(
                contents.Events.Where(e => e.ActionType is not null),
                e =>
                {
                    Assert.False(string.IsNullOrEmpty(e.ActionType));
                    Assert.NotNull(e.PlayerName);
                    Assert.NotNull(e.PlayerIndex);
                });

            var frames = contents.Events.Select(e => e.Frame).ToArray();
            Assert.Equal(frames.OrderBy(f => f), frames);
            Assert.Contains(contents.Events, e => e.Category == "actionDispatched");
        }
        finally
        {
            Cleanup();
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void RunBundle_LoadsAfterDirectoryMove()
    {
        try
        {
            var bundleDirectory = Path.Combine(_root, "bundle");
            var movedDirectory = Path.Combine(_root, "bundle-moved");
            var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA();
            scenario.Seed = 5151;
            scenario.StopConditions.Add(new FrameCapStopCondition(300));
            scenario.Recording = new ScenarioRecordingOptions { Mode = RecordingMode.Full };

            HarnessRunner.RunAndAssert(
                scenario,
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                Path.Combine(_root, "temp"),
                onRunCompleted: null,
                bundleDirectory: bundleDirectory);

            Directory.Move(bundleDirectory, movedDirectory);

            var contents = RunBundle.Load(movedDirectory);
            var game = new TestGame(SageGame.CncGenerals);
            using var reader = new WorldStateArtifactReader(contents.StateArtifactPath, game);
            Assert.Equal(0u, reader.FirstFrame);
            Assert.True(reader.LastFrame > 0);
        }
        finally
        {
            Cleanup();
        }
    }
}
