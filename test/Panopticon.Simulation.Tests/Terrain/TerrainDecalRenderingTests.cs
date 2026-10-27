using System;
using System.IO;
using Panopticon.Graphics;
using Panopticon.Tests.Data;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.TestHarness;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Terrain;

[Collection("SerialGameTests")]
public sealed class TerrainDecalRenderingTests
{
    private readonly ITestOutputHelper _output;

    public TerrainDecalRenderingTests(ITestOutputHelper output) => _output = output;

    [GameFact(SageGame.CncGenerals)]
    public void LegacyScorches_RenderCapture()
    {
        Capture(GraphicsSettings.Legacy(), "legacy");
    }

    [GameFact(SageGame.CncGenerals)]
    public void EnhancedScorches_GoldenGateRejectsDisabledPass()
    {
        var enabled = LoadFrame(Capture(GraphicsSettings.Enhanced(), "enhanced"));
        var disabled = LoadFrame(Capture(GraphicsSettings.Enhanced().WithTerrainDecals(0, TerrainDecalLighting.TerrainLit), "disabled"));
        var comparison = GoldenFrameComparer.Compare(disabled, enabled, ImageQualityMetrics.Score(disabled),
            ImageQualityMetrics.Score(enabled), new GoldenTolerance());
        _output.WriteLine($"Injected disabled-pass regression: {comparison.FailureReason}");
        Assert.False(comparison.Passed);
    }

    private string Capture(GraphicsSettings graphicsSettings, string name)
    {
        var profile = CaptureProfileLoader.LoadEmbeddedCapture("vr-terrain-scorch.json");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "log", "g11-terrain-decals", $"{name}-v2-{Guid.NewGuid():N}");
        var settings = new CaptureRunSettings(null, false, directory, graphicsSettings, FixedRenderClock: true);
        var result = ScenarioCaptureRunner.Run(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
            profile, settings, new ConsoleTestHarnessReporter());
        _output.WriteLine($"Capture: {directory}");
        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(directory, "frame_0001.png")));
        return Path.Combine(directory, "frame_0001.png");
    }

    private static CapturedFrame LoadFrame(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        return new CapturedFrame(pixels, image.Width, image.Height, PixelFormat.R8_G8_B8_A8_UNorm);
    }
}
