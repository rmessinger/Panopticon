using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Panopticon;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.ReplaySketch.ViewModels;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-05: the divergence comparator reports the seeded frame and locus of a known-divergent
/// pair, stays silent for within-tolerance runs, and separates configuration incompatibility
/// from divergence.
/// </summary>
public sealed class RunDivergenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-divergence-{Guid.NewGuid():N}");

    [Fact]
    public void KnownDivergentPair_ReportsExactFrameAndLeafMostLocus()
    {
        var reference = Run(Trees(frames: 11, divergeAt: null));
        var current = Run(Trees(frames: 11, divergeAt: 5));

        var report = RunDivergenceComparator.Compare(reference, current);

        Assert.True(report.Comparable);
        Assert.Equal(5u, report.FirstDivergentFrame);
        var locus = Assert.Single(report.Loci);
        Assert.Equal("world/cell:0,0", locus.Path);
        Assert.Equal("bucket hash differs", locus.Reason);
        Assert.Equal(100UL, locus.ReferenceHash);
        Assert.Equal(999UL, locus.CurrentHash);
    }

    [Fact]
    public void WithinTolerance_IdenticalTrees_ReportNoDivergence()
    {
        var reference = Run(Trees(frames: 11, divergeAt: null));
        var current = Run(Trees(frames: 11, divergeAt: null));

        var report = RunDivergenceComparator.Compare(reference, current);

        Assert.True(report.Comparable);
        Assert.Null(report.FirstDivergentFrame);
        Assert.Empty(report.Loci);
        Assert.Equal(11u, report.FramesCompared);
    }

    [Fact]
    public void MissingOrExtraBuckets_AreDivergences()
    {
        var reference = Run(
            new SignatureTreeSample
            {
                Frame = 0,
                Nodes = [Node("world", 0, 1), Node("cell:0,0", 1, 100)],
            });
        var current = Run(
            new SignatureTreeSample
            {
                Frame = 0,
                Nodes = [Node("world", 0, 1), Node("cell:0,0", 1, 100), Node("cell:2,2", 1, 300)],
            });

        var report = RunDivergenceComparator.Compare(reference, current);

        Assert.True(report.Comparable);
        Assert.Equal(0u, report.FirstDivergentFrame);
        var locus = Assert.Single(report.Loci);
        Assert.Equal("world/cell:2,2", locus.Path);
        Assert.Equal("bucket missing in reference run", locus.Reason);
        Assert.Equal(300UL, locus.CurrentHash);
    }

    [Fact]
    public void MissingBucketsOnReferenceSide_IsDivergence()
    {
        var reference = Run(
            new SignatureTreeSample
            {
                Frame = 0,
                Nodes = [Node("world", 0, 1), Node("cell:0,0", 1, 100), Node("cell:1,0", 1, 200)],
            });
        var current = Run(
            new SignatureTreeSample
            {
                Frame = 0,
                Nodes = [Node("world", 0, 1), Node("cell:0,0", 1, 100)],
            });

        var report = RunDivergenceComparator.Compare(reference, current);

        var locus = Assert.Single(report.Loci);
        Assert.Equal("world/cell:1,0", locus.Path);
        Assert.Equal("bucket missing in current run", locus.Reason);
        Assert.Equal(200UL, locus.ReferenceHash);
        Assert.Equal(0UL, locus.CurrentHash);
    }

    [Fact]
    public void MismatchedT1Configuration_IsIncompatible_NotDivergent()
    {
        var reference = Run(Trees(frames: 5, divergeAt: null));
        var current = Run(Trees(frames: 5, divergeAt: 3));
        current.T1Configuration = new RunSignatureConfigurationRecord { CellSize = 200f };

        var report = RunDivergenceComparator.Compare(reference, current);

        Assert.False(report.Comparable);
        Assert.Null(report.FirstDivergentFrame);
        Assert.Empty(report.Loci);
        Assert.Contains("not comparable", report.IncompatibilityReason);
    }

    [Fact]
    public void MismatchedDomain_IsIncompatible_NotDivergent()
    {
        var reference = Run(Trees(frames: 5, divergeAt: null));
        var current = Run(Trees(frames: 5, divergeAt: null));
        current.DomainId = "transport-graph";

        var report = RunDivergenceComparator.Compare(reference, current);

        Assert.False(report.Comparable);
        Assert.Null(report.FirstDivergentFrame);
        Assert.Contains("different domains", report.IncompatibilityReason);
    }

    [Fact]
    public void FrameRecordedOnOneSide_IsDivergence()
    {
        var reference = Run(Trees(frames: 8, divergeAt: null));
        var current = Run(Trees(frames: 5, divergeAt: null));

        var report = RunDivergenceComparator.Compare(reference, current);

        Assert.True(report.Comparable);
        Assert.Equal(5u, report.FirstDivergentFrame);
        var locus = Assert.Single(report.Loci);
        Assert.Equal("world", locus.Path);
        Assert.Contains("missing from", locus.Reason);
    }

    [Fact]
    public async System.Threading.Tasks.Task ViewModelCompare_ReportsDivergenceThroughBundleManifest()
    {
        var bundleDir = Path.Combine(_root, "bundle");
        TimelineFixture.WriteBundle(bundleDir, lastFrame: 10, snapshotInterval: 5);
        var otherDir = Path.Combine(_root, "other");
        Directory.CreateDirectory(otherDir);
        var other = Run(Trees(frames: 6, divergeAt: null));
        other.T1Configuration = null;
        other.SaveToFile(Path.Combine(otherDir, "run.json"));

        var viewModel = new ResultsViewModel(new StubSelector(null));
        viewModel.BundlePath = Path.Combine(bundleDir, "run.json");
        await viewModel.LoadCommand.ExecuteAsync();
        viewModel.ComparePath = Path.Combine(otherDir, "run.json");
        await viewModel.CompareCommand.ExecuteAsync();

        Assert.NotEmpty(viewModel.DivergenceLines);
        Assert.StartsWith("First divergence at frame", viewModel.DivergenceLines[0], StringComparison.Ordinal);
    }

    private sealed class StubSelector(string? pickedPath) : IBundleFileSelector
    {
        public Task<string?> SelectBundleAsync(System.Threading.CancellationToken cancellationToken) =>
            Task.FromResult(pickedPath);
    }

    private static IEnumerable<SignatureTreeSample> Trees(uint frames, uint? divergeAt)
    {
        for (uint frame = 0; frame < frames; frame++)
        {
            var cellHash = frame == divergeAt ? 999UL : 100UL;
            yield return new SignatureTreeSample
            {
                Frame = frame,
                Nodes = [Node("world", 0, frame == divergeAt ? 9UL : 1UL), Node("cell:0,0", 1, cellHash), Node("cell:1,0", 1, 200)],
            };
        }
    }

    private static SignatureNodeRecord Node(string key, int level, ulong hash) => new() { Key = key, Level = level, Hash = hash };

    private static RunOutput Run(IEnumerable<SignatureTreeSample> trees) => new()
    {
        RunId = Guid.NewGuid().ToString("N")[..8],
        Seed = 1,
        DomainId = "rts",
        SignatureTrees = trees.ToList(),
        T1Configuration = new RunSignatureConfigurationRecord(),
    };

    private static RunOutput Run(params SignatureTreeSample[] trees) => Run((IEnumerable<SignatureTreeSample>)trees);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
