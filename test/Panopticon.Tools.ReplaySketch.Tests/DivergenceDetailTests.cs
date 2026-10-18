using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-22: matching a divergence report to captured T2 windows and deriving delta rows, with
/// distinct diagnostics for missing, unarmed, and incompatible inputs.
/// </summary>
public sealed class DivergenceDetailTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-detail-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static RunDivergenceReport DivergenceAt(uint frame, string path = "world/cell:0,0") => new(
        Comparable: true,
        IncompatibilityReason: null,
        FirstDivergentFrame: frame,
        Loci:
        [
            new DivergenceLocus(frame, path, ReferenceHash: 100, CurrentHash: 999, Reason: "bucket hash differs"),
        ],
        FramesCompared: 30,
        ReferenceLastFrame: 30,
        CurrentLastFrame: 30);

    private static RunOutput ManifestWithWindow(
        uint start,
        uint end,
        string subtree,
        params WorldStateDeltaRecord[] frames)
    {
        var manifest = new RunOutput { RunId = "detail-fixture", Seed = 7, DomainId = "rts" };
        manifest.TriggeredCaptures.Add(new TriggeredDetailWindowRecord
        {
            Marked = false,
            TriggeredAtFrame = end,
            StartFrame = start,
            EndFrame = end,
            DivergedSubtrees = [subtree],
            Frames = frames.ToList(),
            Reason = "divergence",
        });

        return manifest;
    }

    private static WorldObjectStateRecord Object(uint id, float x, float health) => new()
    {
        ObjectId = id,
        DefinitionName = "Thing",
        OwnerIndex = 0,
        X = x,
        Health = health,
        MaxHealth = 100,
    };

    [Fact]
    public void MatchedWindow_SeedsExactFrameSubtreeEntityAndQuantityRows()
    {
        var manifest = ManifestWithWindow(
            10,
            14,
            "cell:0,0",
            new WorldStateDeltaRecord
            {
                Frame = 10,
                Added = [Object(7, 10, 90)],
                PlayerMoney = [1000, 2000],
            },
            new WorldStateDeltaRecord
            {
                Frame = 12,
                Changed = [Object(7, 14, 80)],
            },
            new WorldStateDeltaRecord
            {
                Frame = 13,
                Removed = [7],
            });

        var report = DivergenceDetailInspector.Inspect(DivergenceAt(12), manifest);

        Assert.Equal(DivergenceDetailState.Matched, report.State);
        Assert.Equal(10u, report.WindowStart);
        Assert.Equal(14u, report.WindowEnd);
        Assert.Equal("divergence", report.WindowReason);

        var health = Assert.Single(report.Rows, r => r.EntityId == 7 && r.Quantity == "health");
        Assert.Equal(12u, health.Frame);
        Assert.Equal("cell:0,0", health.Subtree);
        Assert.Equal("90", health.Before);
        Assert.Equal("80", health.Current);
        Assert.Equal("-10.0", health.Delta);

        var position = Assert.Single(report.Rows, r => r.EntityId == 7 && r.Quantity == "position");
        Assert.Equal(12u, position.Frame);
        Assert.Equal("moved 4", position.Delta);

        Assert.Contains(report.Rows, r => r.Frame == 10 && r.Quantity == "added" && r.Delta == "+1");
        Assert.Contains(report.Rows, r => r.Frame == 13 && r.Quantity == "removed" && r.Delta == "-1");
        Assert.Contains(report.Rows, r => r.Frame == 10 && r.Quantity == "playerMoney");
    }

    [Fact]
    public void MissingCapture_HasDistinctDiagnostic()
    {
        var manifest = new RunOutput { RunId = "bare", Seed = 1, DomainId = "rts" };

        var report = DivergenceDetailInspector.Inspect(DivergenceAt(12), manifest);

        Assert.Equal(DivergenceDetailState.Missing, report.State);
        Assert.Empty(report.Rows);
        Assert.Null(report.WindowStart);
        Assert.Contains("no T2 detail windows", report.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void UnarmedWindow_HasDistinctDiagnostic()
    {
        // A window exists, but it covers a different frame range and subtree.
        var manifest = ManifestWithWindow(20, 24, "cell:5,5", new WorldStateDeltaRecord { Frame = 20 });

        var report = DivergenceDetailInspector.Inspect(DivergenceAt(12), manifest);

        Assert.Equal(DivergenceDetailState.Unarmed, report.State);
        Assert.Empty(report.Rows);
        Assert.Contains("none covers frame 12", report.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompatibleReport_PassesReasonThrough()
    {
        var manifest = ManifestWithWindow(10, 14, "cell:0,0", new WorldStateDeltaRecord { Frame = 10 });
        var divergence = new RunDivergenceReport(
            Comparable: false,
            IncompatibilityReason: "T1 signature configuration differs",
            FirstDivergentFrame: 0,
            Loci: [],
            FramesCompared: 0,
            ReferenceLastFrame: 30,
            CurrentLastFrame: 30);

        var report = DivergenceDetailInspector.Inspect(divergence, manifest);

        Assert.Equal(DivergenceDetailState.Incompatible, report.State);
        Assert.Equal("T1 signature configuration differs", report.Diagnostic);
        Assert.Empty(report.Rows);
    }

    [Fact]
    public void RowSelection_DataCarriesFramesForTimelineSync()
    {
        // The quantity rows carry the frame they were observed at; the view routes selection
        // through ResultsViewModel.ScrubTo, so frames on rows are the sync contract.
        var manifest = ManifestWithWindow(
            10,
            12,
            "cell:0,0",
            new WorldStateDeltaRecord { Frame = 10, Added = [Object(3, 1, 50)] },
            new WorldStateDeltaRecord { Frame = 11, Changed = [Object(3, 2, 45)] });

        var report = DivergenceDetailInspector.Inspect(DivergenceAt(11), manifest);

        Assert.Equal(DivergenceDetailState.Matched, report.State);
        Assert.All(report.Rows, row => Assert.InRange(row.Frame, 10u, 11u));
        var health = Assert.Single(report.Rows, r => r.Quantity == "health");
        Assert.Equal(11u, health.Frame);
    }
}
