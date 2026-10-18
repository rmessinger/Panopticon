using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class ResultsExportTests : System.IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-export-{System.Guid.NewGuid():N}");

    [Fact]
    public async Task Export_WritesFrameChartAndSelfContainedHtml()
    {
        var bundle = Path.Combine(_root, "bundle");
        TimelineFixture.WriteBundle(bundle, lastFrame: 30, snapshotInterval: 10);

        var result = await ResultsExportService.ExportAsync(
            Path.Combine(bundle, "run.json"), 17, Path.Combine(_root, "out1"));

        Assert.True(File.Exists(result.FrameImagePath));
        Assert.True(File.Exists(result.ChartImagePath));
        Assert.True(File.Exists(result.ReportHtmlPath));
        Assert.True(new FileInfo(result.FrameImagePath).Length > 1000);
        Assert.True(new FileInfo(result.ChartImagePath).Length > 1000);
        Assert.Equal(17u, result.Frame);
        Assert.True(result.SeriesCount >= 3);

        var html = await File.ReadAllTextAsync(result.ReportHtmlPath);
        Assert.Contains("data:image/png;base64,", html);
        Assert.Contains("timeline-fixture", html);
        Assert.Contains("money:USA", html);
        Assert.Contains("AmericaCommandCenter", html);
        Assert.DoesNotContain("src=\"http", html);
    }

    [Fact]
    public async Task Export_IsByteIdenticalAcrossRuns()
    {
        var bundle = Path.Combine(_root, "bundle");
        TimelineFixture.WriteBundle(bundle, lastFrame: 30, snapshotInterval: 10);
        var manifest = Path.Combine(bundle, "run.json");

        var first = await ResultsExportService.ExportAsync(manifest, 17, Path.Combine(_root, "a"));
        var second = await ResultsExportService.ExportAsync(manifest, 17, Path.Combine(_root, "b"));

        Assert.True(File.ReadAllBytes(first.FrameImagePath)
            .SequenceEqual(File.ReadAllBytes(second.FrameImagePath)));
        Assert.True(File.ReadAllBytes(first.ChartImagePath)
            .SequenceEqual(File.ReadAllBytes(second.ChartImagePath)));
        Assert.True(File.ReadAllBytes(first.ReportHtmlPath)
            .SequenceEqual(File.ReadAllBytes(second.ReportHtmlPath)));
    }

    [Fact]
    public async Task Export_ClampsFrameBeyondLastRecordedFrame()
    {
        var bundle = Path.Combine(_root, "bundle");
        TimelineFixture.WriteBundle(bundle, lastFrame: 30, snapshotInterval: 10);

        var result = await ResultsExportService.ExportAsync(bundle, 9999, Path.Combine(_root, "out"));

        Assert.Equal(30u, result.Frame);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
