#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Recording;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// One metric line on the results chart (WI-A5-04): the samples of one
/// <c>series:key</c> pair from the run manifest's domain metrics, plus the player money series.
/// </summary>
public sealed record MetricSeriesRecord(
    string Name,
    IReadOnlyList<(uint Frame, double Value)> Points);

/// <summary>
/// Scrubbable timeline over a WI-A5-09 run bundle (WI-A5-04): the manifest's metric samples and
/// events filtered through the current frame, and the recorded state at the current frame via
/// the indexed artifact reader. Loading never boots an engine game: state records are primitive
/// persistence, so the reader takes the <see cref="SageGame"/> alone.
/// </summary>
public sealed class RunBundleTimeline : IDisposable
{
    private readonly WorldStateArtifactReader _reader;
    private readonly RunStateCursor _cursor;

    public RunBundleTimeline(RunBundleContents bundle, SageGame sageGame)
    {
        Bundle = bundle;
        _reader = new WorldStateArtifactReader(bundle.StateArtifactPath, sageGame);
        _cursor = new RunStateCursor(_reader);
        Series = ExtractSeries(bundle.Manifest);
        CurrentFrame = _cursor.FirstFrame;
    }

    public RunBundleContents Bundle { get; }

    public IReadOnlyList<MetricSeriesRecord> Series { get; }

    public uint FirstFrame => _cursor.FirstFrame;

    public uint LastFrame => _cursor.LastFrame;

    public uint CurrentFrame { get; private set; }

    public bool IsTruncated => _reader.IsTruncated;

    public string? TruncationDiagnostic => _reader.TruncationDiagnostic;

    public WorldStateSummary? CurrentState { get; private set; }

    /// <summary>Seeks to a frame, clamped to the recorded range, and updates the current state.</summary>
    public WorldStateSummary SeekTo(uint frame)
    {
        CurrentState = _cursor.SeekTo(frame);
        CurrentFrame = _cursor.CurrentFrame;
        return CurrentState;
    }

    public IReadOnlyList<RunEventRecord> EventsThrough(uint frame) =>
        Bundle.Events.Where(e => e.Frame <= frame).ToList();

    /// <summary>One series per <c>series:key</c> pair plus one money series per player index.</summary>
    private static IReadOnlyList<MetricSeriesRecord> ExtractSeries(RunOutput manifest)
    {
        var series = new List<MetricSeriesRecord>();
        foreach (var group in manifest.MetricSamples
                     .SelectMany(s => s.DomainMetrics, (sample, metric) => (sample.Frame, metric))
                     .GroupBy(pair => pair.metric.Series + ":" + pair.metric.Key))
        {
            series.Add(new MetricSeriesRecord(
                group.Key,
                group.OrderBy(pair => pair.Frame)
                    .Select(pair => (pair.Frame, pair.metric.Value))
                    .ToList()));
        }

        var playerCount = manifest.MetricSamples.Max(s => s.Players.Count);
        for (var player = 0; player < playerCount; player++)
        {
            var name = manifest.MetricSamples
                .Where(s => s.Players.Count > player)
                .Select(s => s.Players[player].Player)
                .FirstOrDefault(n => n != null);
            var points = manifest.MetricSamples
                .Where(s => s.Players.Count > player)
                .Select(s => (s.Frame, (double)s.Players[player].Money))
                .ToList();
            if (points.Count > 0)
            {
                series.Add(new MetricSeriesRecord("money:" + (name ?? "player" + player), points));
            }
        }

        return series;
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}
