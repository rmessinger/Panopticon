using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Panopticon;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.ReplaySketch.ViewModels;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>WI-A5-18: recorded-frame projection into card-12 primitives.</summary>
public sealed class RecordedStateProjectorTests
{
    [Fact]
    public void NullOrEmptyState_ProjectsNothing()
    {
        Assert.True(RecordedStateProjector.Project(null, null).Primitives.Count == 0);
        Assert.True(RecordedStateProjector.Project(null, null).Points.Count == 0);

        var empty = new WorldStateSummary { Frame = 3 };
        var projection = RecordedStateProjector.Project(empty, null);
        Assert.True(projection.Primitives.Count == 0);
        Assert.True(projection.Points.Count == 0);
    }

    [Fact]
    public void Buckets_MirrorRtsTopologyKeys_AtConfiguredCellSize()
    {
        var state = new WorldStateSummary { Frame = 1 };
        state.Objects.AddRange(new[]
        {
            Object(1, "Thing1", 0, 50f, 0f),
            Object(2, "Thing2", 0, 250f, 0f),
            Object(3, "Thing3", 1, 50f, 120f),
        });

        var projection = RecordedStateProjector.Project(state, null);

        Assert.Equal("cell:0,0", projection.Primitives[0].BucketKey);
        Assert.Equal("cell:2,0", projection.Primitives[1].BucketKey);
        Assert.Equal("cell:0,1", projection.Primitives[2].BucketKey);
        Assert.Equal(["cell:0,0", "cell:2,0", "cell:0,1"], projection.Points.Select(p => p.Point.BucketKey).ToArray());
    }

    [Fact]
    public void ConfigurationCellSize_ChangesBucketAssignment()
    {
        var state = new WorldStateSummary { Frame = 1 };
        state.Objects.Add(Object(7, "Moved", 0, 150f, 0f));

        Assert.Equal("cell:1,0", RecordedStateProjector.Project(state, null).Primitives[0].BucketKey);

        var configured = new RunSignatureConfigurationRecord { CellSize = 200f };
        Assert.Equal("cell:0,0", RecordedStateProjector.Project(state, configured).Primitives[0].BucketKey);
    }

    [Fact]
    public void Points_CarryOwnerKindAndIdentity()
    {
        var state = new WorldStateSummary { Frame = 1 };
        state.Objects.AddRange(new[]
        {
            Object(11, "AmericaTankCrusader", 0, 10f, 10f),
            Object(12, "GLATankScorpion", 1, 20f, 20f),
        });

        var projection = RecordedStateProjector.Project(state, null);

        Assert.Equal(11u, projection.Points[0].ObjectId);
        Assert.Equal("AmericaTankCrusader", projection.Points[0].Point.KindName);
        Assert.Equal(0, projection.Points[0].Point.OwnerIndex);
        Assert.Equal(12u, projection.Points[1].ObjectId);
        Assert.Equal(1, projection.Points[1].Point.OwnerIndex);
    }

    [Fact]
    public async Task Scrubbing_ChangesProjectionWithTheRecordedState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rs-projector-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            TimelineFixture.WriteBundle(Path.Combine(root, "bundle"), lastFrame: 30, snapshotInterval: 10);
            var viewModel = new ResultsViewModel(new StubSelector(null));
            viewModel.BundlePath = Path.Combine(root, "bundle", RunBundle.ManifestFileName);
            await viewModel.LoadCommand.ExecuteAsync();

            viewModel.ScrubTo(7);
            var at7 = viewModel.FrameProjection;
            Assert.Contains(10u, at7.Points.Select(p => p.ObjectId));

            viewModel.ScrubTo(17);
            var at17 = viewModel.FrameProjection;
            Assert.DoesNotContain(10u, at17.Points.Select(p => p.ObjectId));
            Assert.Contains(1u, at17.Points.Select(p => p.ObjectId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static WorldObjectState Object(uint id, string definition, int owner, float x, float y) =>
        new()
        {
            ObjectId = id,
            DefinitionName = definition,
            OwnerIndex = owner,
            Position = new System.Numerics.Vector3(x, y, 0f),
            Health = 100f,
            MaxHealth = 100f,
        };

    private sealed record StubSelector(string? Path) : IBundleFileSelector
    {
        public Task<string?> SelectBundleAsync(System.Threading.CancellationToken cancellationToken) =>
            Task.FromResult(Path);
    }
}
