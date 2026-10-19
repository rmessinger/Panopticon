# nullable enable

using System;
using System.IO;
using System.Linq;
using Panopticon.Tests.Data;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-13: the bounded preview runs a real short engagement through the harness bundle path
/// and projects frame-zero and final state through the same projector the Results view uses.
/// Engine-free rejection/format coverage lives in ScenarioPreviewTests.
/// </summary>
[Collection("SerialGameTests")]
public class ScenarioPreviewHeadlessTests : MockedGameTest
{
    private readonly ITestOutputHelper _output;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-preview-headless-{Guid.NewGuid():N}");

    public ScenarioPreviewHeadlessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GameFact(SageGame.CncGenerals)]
    public void ValidScenario_ReturnsFrameZeroAndFinalState()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA();
        scenario.Seed = 4545;

        var result = ScenarioPreviewService.Preview(scenario, installation, _root, frameBudget: 45);

        _output.WriteLine($"ran={result.Ran} frames={result.FramesCompleted} wall={result.WallClockMilliseconds} ms");
        Assert.True(result.Ran);
        // The harness budget is cap+1 frames (0..45 inclusive), so a budget-capped preview
        // reports 46 completed logic frames.
        Assert.InRange(result.FramesCompleted, 1u, 46u);
        Assert.True(result.HitFrameBudget);
        Assert.Contains(result.Diagnostics, d => d.Contains("frame budget reached", StringComparison.Ordinal));

        Assert.NotNull(result.FrameZero);
        Assert.Equal(0u, result.FrameZero!.Frame);
        Assert.True(result.FrameZero.EntityCount > 0);

        Assert.NotNull(result.Final);
        Assert.True(result.Final!.EntityCount > 0);
        Assert.True(result.Final.RegionKeys.Count > 0);
        Assert.All(result.Final.RegionKeys, key => key.StartsWith("cell:", StringComparison.Ordinal));
    }

    public void DisposeTestRoot()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
