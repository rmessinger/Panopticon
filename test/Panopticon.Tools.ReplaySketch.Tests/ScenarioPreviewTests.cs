# nullable enable
using System;
using System.IO;
using System.Linq;
using Panopticon.Logic.Orders;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-13: the bounded scenario preview rejects invalid scenarios structurally before any
/// installation or host work, reports missing installations, and formats its summary lines.
/// Install-backed run coverage lives in ScenarioPreviewHeadlessTests.
/// </summary>
public class ScenarioPreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-preview-{Guid.NewGuid():N}");

    [Fact]
    public void InvalidScenario_ReportsFieldPaths_BeforeAnyInstallationWork()
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA();
        scenario.Players[0].Actions.Add(NewAction("dupe"));
        scenario.Players[0].Actions.Add(NewAction("dupe"));

        var result = ScenarioPreviewService.Preview(scenario, installation: null, _root);

        Assert.False(result.Ran);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, d => d.Contains("players[0].actions[", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, d => d.Contains("no installation", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidScenario_WithoutInstallation_ReportsNotRun()
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA();

        var result = ScenarioPreviewService.Preview(scenario, installation: null, _root);

        Assert.False(result.Ran);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("no installation", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatSummary_CoversNotRun_FrameZero_Final_AndBudgetLines()
    {
        var notRun = ScenarioPreviewResult.NotRun(["map load failed: boom"]);
        Assert.StartsWith("Preview not run: map load failed: boom", ScenarioPreviewService.FormatSummary(notRun), StringComparison.Ordinal);

        var frameZero = new ScenarioPreviewProjection(0, 3, ["cell:0,0"]);
        var final = new ScenarioPreviewProjection(60, 2, ["cell:0,0"]);
        var ran = new ScenarioPreviewResult(
            Ran: true,
            FramesCompleted: 60,
            HitFrameBudget: true,
            ExceededTimeBudget: true,
            WallClockMilliseconds: 4321,
            FrameZero: frameZero,
            Final: final,
            Diagnostics: ["frame budget reached (60 frames)", "exceeded time budget: 4321 ms > 30000 ms"]);
        var text = ScenarioPreviewService.FormatSummary(ran);

        var lines = text.Split(Environment.NewLine);
        Assert.Equal("Preview: 60 frames in 4321 ms", lines[0]);
        Assert.Equal("Frame 0: 3 entities", lines[1]);
        Assert.Equal("Final: 2 entities in 1 region(s)", lines[2]);
        Assert.Equal("frame budget reached (60 frames)", lines[3]);
        Assert.Equal("exceeded time budget: 4321 ms > 30000 ms", lines[4]);
    }

    private static ActionEntry NewAction(string id)
    {
        return new ActionEntry(
            "preview probe",
            CommandSpec.CreateOpen(OrderType.MoveTo, new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
            position: null,
            timing: new FixedTiming(100),
            id: id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
