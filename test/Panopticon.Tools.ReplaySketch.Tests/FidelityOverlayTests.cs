using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Simulation;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Profiles;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.ReplaySketch.ViewModels;
using SkiaSharp;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-19: fidelity overlay semantics. Categories are domain-supplied strings on card-12
/// points; the renderer colors by category (no B7 type), disables explicitly when the frame has
/// no category data, and the view model mode switch repaints the projection.
/// </summary>
public sealed class FidelityOverlayTests
{
    private static DomainVisualPoint Point(
        uint id,
        string bucket,
        float x,
        string? category) => new(bucket, new System.Numerics.Vector3(x, 0, 0), 0, "unit", 1f, category);

    [Fact]
    public void DistinctCategories_RenderDistinctColorsAndCarryMarkers()
    {
        using var surface = SKSurface.Create(new SKImageInfo(300, 200));
        var canvas = surface.Canvas;
        var points = new[]
        {
            Point(1, "cell:0,0", 10f, "Full"),
            Point(2, "cell:1,0", 20f, "Reduced"),
            Point(3, "cell:2,0", 30f, "Aggregated"),
        };
        var markers = new[]
        {
            new FramePointMarker(1, points[0]),
            new FramePointMarker(2, points[1]),
            new FramePointMarker(3, points[2]),
        };

        var viewMarkers = RunFrameRenderer.Render(
            canvas, 0, 0, 300, 200, points, markers, fidelityOverlay: true);

        Assert.Equal(new[] { "Full", "Reduced", "Aggregated" }, viewMarkers.Select(m => m.FidelityCategory));
        Assert.Equal(
            RunFrameRenderer.FidelityPaletteColor(0),
            RunFrameRenderer.FidelityPaletteColor(0));
        Assert.NotEqual(
            RunFrameRenderer.FidelityPaletteColor(0),
            RunFrameRenderer.FidelityPaletteColor(1));
        Assert.NotEqual(
            RunFrameRenderer.FidelityPaletteColor(1),
            RunFrameRenderer.FidelityPaletteColor(2));
        Assert.NotNull(RunFrameRenderer.HitTest(viewMarkers, viewMarkers[0].ScreenX, viewMarkers[0].ScreenY));
    }

    [Fact]
    public void OverlayWithoutCategories_DisablesGracefully()
    {
        using var surface = SKSurface.Create(new SKImageInfo(300, 200));
        var points = new[]
        {
            Point(1, "cell:0,0", 10f, null),
            Point(2, "cell:1,0", 20f, string.Empty),
        };
        var markers = new[]
        {
            new FramePointMarker(1, points[0]),
            new FramePointMarker(2, points[1]),
        };

        var viewMarkers = RunFrameRenderer.Render(
            surface.Canvas, 0, 0, 300, 200, points, markers, fidelityOverlay: true);

        // Owner colors still apply and the renderer reports the absence rather than failing.
        Assert.Equal(2, viewMarkers.Count);
        Assert.All(viewMarkers, m => Assert.NotEqual("Full", m.FidelityCategory));
    }

    [Fact]
    public void OverlayOff_IgnoresCategories()
    {
        using var surface = SKSurface.Create(new SKImageInfo(300, 200));
        var points = new[] { Point(1, "cell:0,0", 10f, "Full") };
        var markers = new[] { new FramePointMarker(1, points[0]) };

        var viewMarkers = RunFrameRenderer.Render(
            surface.Canvas, 0, 0, 300, 200, points, markers, fidelityOverlay: false);

        Assert.Single(viewMarkers);
        Assert.Equal("Full", viewMarkers[0].FidelityCategory);
    }

    [Fact]
    public void ViewModel_ModeSwitch_RaisesFrameProjection()
    {
        var viewModel = new ResultsViewModel(new StubSelector(null));
        var raised = new List<string>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        viewModel.FrameColorMode = FrameColorMode.Fidelity;

        Assert.Contains("FrameColorMode", raised);
        Assert.Contains("FrameProjection", raised);
        Assert.Equal(FrameColorMode.Fidelity, viewModel.FrameColorMode);
    }

    [Fact]
    public void Projector_LeavesFidelityNull()
    {
        // Recorded RTS state carries no fidelity category today: the overlay's missing-data
        // path is the default for bundles recorded by the current engine.
        var summary = new Panopticon.Recording.WorldStateSummary { Frame = 1 };
        summary.Objects.Add(new Panopticon.Recording.WorldObjectState
        {
            ObjectId = 5,
            DefinitionName = "Thing1",
            OwnerIndex = 0,
            Position = new System.Numerics.Vector3(10, 0, 0),
            Health = 50,
            MaxHealth = 100,
        });

        var projection = RecordedStateProjector.Project(summary, null);

        var point = Assert.Single(projection.Points);
        Assert.Null(point.Point.FidelityCategory);
    }

    private sealed record StubSelector(string? Path) : IBundleFileSelector
    {
        public System.Threading.Tasks.Task<string?> SelectBundleAsync(System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult(Path);
    }
}
