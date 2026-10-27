using System;
using System.IO;
using Panopticon.Graphics;
using Panopticon.Tests.Data;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.TestHarness;
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
        var profile = CaptureProfileLoader.LoadEmbeddedCapture("vr-terrain-scorch.json");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "log", "g11-terrain-decals", $"legacy-v1-{Guid.NewGuid():N}");
        var settings = new CaptureRunSettings(null, false, directory, GraphicsSettings.Legacy(), FixedRenderClock: true);
        var result = ScenarioCaptureRunner.Run(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
            profile, settings, new ConsoleTestHarnessReporter());
        _output.WriteLine($"Capture: {directory}");
        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(directory, "frame_0001.png")));
    }
}
