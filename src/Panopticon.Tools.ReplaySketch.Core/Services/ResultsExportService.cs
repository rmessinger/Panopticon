#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Panopticon;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Profiles;
using SkiaSharp;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>One exported artifact set, all paths inside <paramref name="OutputDirectory"/>.</summary>
public sealed record ResultsExportResult(
    string OutputDirectory,
    string FrameImagePath,
    string ChartImagePath,
    string ReportHtmlPath,
    uint Frame,
    int SeriesCount,
    int EventCount);

/// <summary>
/// Card WI-A5-20: exports one run bundle's selected frame headlessly. The frame view and metric
/// chart raster through the same CPU Skia renderers the Results tab uses (RunFrameRenderer,
/// MetricChartRenderer), and the report is a single self-contained HTML file with the images
/// inlined. Output is deterministic for identical input: fixed image sizes, invariant-culture
/// formatting, ordinal sorts, and no timestamps.
/// </summary>
public static class ResultsExportService
{
    private const int FrameImageWidth = 760;
    private const int FrameImageHeight = 480;
    private const int ChartImageWidth = 760;
    private const int ChartImageHeight = 320;

    /// <summary>
    /// Exports frame.png, chart.png, and report.html for <paramref name="frame"/> of the bundle
    /// at <paramref name="bundlePath"/> (bundle directory or run.json path, see RunBundle.Load).
    /// </summary>
    public static async Task<ResultsExportResult> ExportAsync(
        string bundlePath,
        uint frame,
        string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundlePath);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var contents = RunBundle.Load(bundlePath)
            ?? throw new InvalidOperationException($"Run bundle not loadable: {bundlePath}");

        using var timeline = new RunBundleTimeline(contents, SageGame.CncGenerals);
        var clampedFrame = Math.Min(frame, timeline.LastFrame);
        var state = timeline.SeekTo(clampedFrame);
        var projection = RecordedStateProjector.Project(state, contents.Manifest.T1Configuration);

        var frameImagePath = Path.Combine(outputDirectory, "frame.png");
        var chartImagePath = Path.Combine(outputDirectory, "chart.png");
        var reportPath = Path.Combine(outputDirectory, "report.html");

        WriteFrameImage(frameImagePath, projection.Primitives, projection.Points);
        WriteChartImage(timeline.Series, clampedFrame, chartImagePath);
        await File.WriteAllTextAsync(
            reportPath,
            BuildReportHtml(contents, timeline.Series, clampedFrame, frameImagePath, chartImagePath))
            .ConfigureAwait(false);

        return new ResultsExportResult(
            outputDirectory,
            frameImagePath,
            chartImagePath,
            reportPath,
            clampedFrame,
            timeline.Series.Count,
            contents.Events.Count);
    }

    private static void WriteFrameImage(
        string path,
        IReadOnlyList<DomainVisualPrimitive>? primitives,
        IReadOnlyList<FramePointMarker>? points)
    {
        using var surface = SKSurface.Create(new SKImageInfo(FrameImageWidth, FrameImageHeight));
        var canvas = surface.Canvas;
        canvas.Clear();
        RunFrameRenderer.Render(
            canvas,
            0,
            0,
            FrameImageWidth,
            FrameImageHeight,
            primitives ?? [],
            points);
        EncodePng(surface, path);
    }

    private static void WriteChartImage(IReadOnlyList<MetricSeriesRecord> series, uint frame, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ChartImageWidth, ChartImageHeight));
        var canvas = surface.Canvas;
        canvas.Clear();
        MetricChartRenderer.Render(
            canvas,
            0,
            0,
            ChartImageWidth,
            ChartImageHeight,
            series.Select(s => (s.Name, s.Points)).ToList(),
            frame);
        EncodePng(surface, path);
    }

    private static void EncodePng(SKSurface surface, string path)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static string BuildReportHtml(
        RunBundleContents contents,
        IReadOnlyList<MetricSeriesRecord> series,
        uint frame,
        string frameImagePath,
        string chartImagePath)
    {
        var manifest = contents.Manifest;
        var invariant = CultureInfo.InvariantCulture;
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.AppendLine("<title>Run report</title>");
        html.AppendLine("<style>body{font-family:sans-serif;margin:24px;color:#222}");
        html.AppendLine("table{border-collapse:collapse;margin:12px 0}");
        html.AppendLine("th,td{border:1px solid #ccc;padding:4px 10px;text-align:left}");
        html.AppendLine("img{max-width:760px;border:1px solid #ccc}</style></head><body>");
        html.AppendLine("<h1>Run report</h1>");
        html.Append("<p>Run <code>").Append(Escape(manifest.RunId)).Append("</code>");
        html.Append(", seed <code>").Append(manifest.Seed.ToString(invariant)).Append("</code>");
        html.Append(", scenario <code>").Append(Escape(manifest.ScenarioId ?? string.Empty)).Append("</code>");
        html.Append(", domain <code>").Append(Escape(manifest.DomainId ?? string.Empty)).Append("</code>");
        html.Append(", frames <code>").Append(manifest.TotalLogicFrames.ToString(invariant));
        html.Append("</code>, exported frame <code>").Append(frame.ToString(invariant));
        html.AppendLine("</code>.</p>");
        html.AppendLine($"<h2>Frame view (frame {frame.ToString(invariant)})</h2>");
        html.AppendLine($"<img src=\"data:image/png;base64,{EncodeBase64(frameImagePath)}\" alt=\"frame view\">");
        html.AppendLine("<h2>Metric chart</h2>");
        html.AppendLine($"<img src=\"data:image/png;base64,{EncodeBase64(chartImagePath)}\" alt=\"metric chart\">");
        html.AppendLine("<h2>Metric series</h2>");
        html.AppendLine("<table><tr><th>Series</th><th>Points</th><th>First</th><th>Last</th></tr>");
        foreach (var entry in series.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            var first = entry.Points.Count > 0
                ? entry.Points[0].Value.ToString("0.##", invariant)
                : string.Empty;
            var last = entry.Points.Count > 0
                ? entry.Points[^1].Value.ToString("0.##", invariant)
                : string.Empty;
            html.Append("<tr><td>").Append(Escape(entry.Name)).Append("</td><td>");
            html.Append(entry.Points.Count.ToString(invariant)).Append("</td><td>");
            html.Append(first).Append("</td><td>").Append(last).Append("</td></tr>");
        }

        html.AppendLine("</table>");
        html.AppendLine("<h2>Events</h2>");
        html.AppendLine("<table><tr><th>Frame</th><th>Category</th><th>Action</th><th>Player</th><th>Definition</th></tr>");
        foreach (var evt in contents.Events.OrderBy(e => e.Frame, Comparer<uint>.Default)
            .ThenBy(e => e.Category, StringComparer.Ordinal))
        {
            html.Append("<tr><td>").Append(evt.Frame.ToString(invariant));
            html.Append("</td><td>").Append(Escape(evt.Category));
            html.Append("</td><td>").Append(Escape(evt.ActionType ?? string.Empty));
            html.Append("</td><td>").Append(Escape(evt.PlayerName ?? string.Empty));
            html.Append("</td><td>").Append(Escape(evt.DefinitionName ?? string.Empty));
            html.AppendLine("</td></tr>");
        }

        html.AppendLine("</table>");
        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static string EncodeBase64(string path) =>
        Convert.ToBase64String(File.ReadAllBytes(path));

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
