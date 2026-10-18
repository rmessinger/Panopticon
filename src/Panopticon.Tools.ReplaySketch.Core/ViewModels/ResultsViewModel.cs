#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.ReplaySketch.ViewModels;

/// <summary>
/// Card WI-A5-04: view model behind the results view. Loads a WI-A5-09 run bundle, exposes the
/// timeline (frame bounds, scrub, current state), the metric series extracted from the manifest,
/// and the event log filtered through the current frame. All logic lives here in Core; the
/// Avalonia view is a thin shell.
/// </summary>
public sealed class ResultsViewModel : IDisposable
{
    private readonly IBundleFileSelector _bundleSelector;
    private readonly IPlaybackClock? _playbackClock;
    private RunBundleTimeline? _timeline;
    private ResultsPlaybackController? _playback;
    private string? _bundlePath;
    private string? _comparePath;
    private string _statusMessage = string.Empty;
    private uint _selectedFrame;
    private IReadOnlyList<RunEventRecord> _eventsThroughFrame = [];
    private IReadOnlyList<MetricSeriesRecord> _series = [];
    private IReadOnlyList<string> _divergenceLines = [];
    private DivergenceDetailReport? _divergenceDetail;
    private RecordedFrameProjection _frameProjection = RecordedFrameProjection.Empty;
    private uint? _selectedPointId;
    private FrameColorMode _frameColorMode = FrameColorMode.Owner;

    public ResultsViewModel(IBundleFileSelector bundleSelector, IPlaybackClock? playbackClock = null)
    {
        _bundleSelector = bundleSelector ?? throw new ArgumentNullException(nameof(bundleSelector));
        _playbackClock = playbackClock;
        LoadCommand = new AsyncCommand(_ => LoadAsync());
        BrowseCommand = new AsyncCommand(_ => BrowseAsync());
        CompareCommand = new AsyncCommand(_ => CompareAsync());
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public AsyncCommand LoadCommand { get; }

    public AsyncCommand BrowseCommand { get; }

    public string? BundlePath
    {
        get => _bundlePath;
        set
        {
            if (_bundlePath == value)
            {
                return;
            }

            _bundlePath = value;
            OnChanged(nameof(BundlePath));
        }
    }

    public RunBundleTimeline? Timeline
    {
        get => _timeline;
        private set
        {
            _timeline = value;
            OnChanged(nameof(Timeline));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnChanged(nameof(StatusMessage));
        }
    }

    public uint SelectedFrame
    {
        get => _selectedFrame;
        set
        {
            if (_selectedFrame == value)
            {
                return;
            }

            _selectedFrame = value;
            OnChanged(nameof(SelectedFrame));
        }
    }

    public IReadOnlyList<MetricSeriesRecord> Series
    {
        get => _series;
        private set
        {
            _series = value;
            OnChanged(nameof(Series));
        }
    }

    public IReadOnlyList<RunEventRecord> EventsThroughFrame
    {
        get => _eventsThroughFrame;
        private set
        {
            _eventsThroughFrame = value;
            OnChanged(nameof(EventsThroughFrame));
        }
    }

    public bool HasTimeline => _timeline != null;

    /// <summary>WI-A5-31: playback controller for the loaded timeline, null without a clock or bundle.</summary>
    public ResultsPlaybackController? Playback
    {
        get => _playback;
        private set
        {
            _playback = value;
            OnChanged(nameof(Playback));
        }
    }

    public string? ComparePath
    {
        get => _comparePath;
        set
        {
            if (_comparePath == value)
            {
                return;
            }

            _comparePath = value;
            OnChanged(nameof(ComparePath));
        }
    }

    public IReadOnlyList<string> DivergenceLines
    {
        get => _divergenceLines;
        private set
        {
            _divergenceLines = value;
            OnChanged(nameof(DivergenceLines));
        }
    }

    /// <summary>
    /// WI-A5-22: T2 capture detail matched to the latest divergence report; null before a
    /// comparison or when it failed. Rows carry frames so selection can scrub the timeline.
    /// </summary>
    public DivergenceDetailReport? DivergenceDetail
    {
        get => _divergenceDetail;
        private set
        {
            _divergenceDetail = value;
            OnChanged(nameof(DivergenceDetail));
        }
    }

    public IReadOnlyList<DivergenceDetailRow> DivergenceDetailRows => DivergenceDetail?.Rows ?? [];

    /// <summary>WI-A5-18: card-12 primitives for the frame at the current scrub position.</summary>
    public RecordedFrameProjection FrameProjection
    {
        get => _frameProjection;
        private set
        {
            _frameProjection = value;
            OnChanged(nameof(FrameProjection));
        }
    }

    /// <summary>WI-A5-18: entity id of the hit-tested point, null when nothing is selected.</summary>
    public uint? SelectedPointId
    {
        get => _selectedPointId;
        set
        {
            if (_selectedPointId == value)
            {
                return;
            }

            _selectedPointId = value;
            OnChanged(nameof(SelectedPointId));
            OnChanged(nameof(FrameProjection));
        }
    }

    /// <summary>WI-A5-19: how the frame view colors points; switching repaints the projection.</summary>
    public FrameColorMode FrameColorMode
    {
        get => _frameColorMode;
        set
        {
            if (_frameColorMode == value)
            {
                return;
            }

            _frameColorMode = value;
            OnChanged(nameof(FrameColorMode));
            OnChanged(nameof(FrameProjection));
        }
    }

    /// <summary>WI-A5-05: compares the loaded bundle's T1 trees against a second run manifest.</summary>
    public AsyncCommand CompareCommand { get; }

    public void ScrubTo(uint frame)
    {
        if (_timeline == null)
        {
            return;
        }

        _timeline.SeekTo(frame);
        ApplyTimelinePosition();
    }

    public void Dispose()
    {
        _playback?.Dispose();
        _playback = null;
        _timeline?.Dispose();
        _timeline = null;
    }

    private async System.Threading.Tasks.Task CompareAsync()
    {
        if (_timeline == null)
        {
            DivergenceLines = new[] { "Load a bundle before comparing." };
            return;
        }

        if (string.IsNullOrWhiteSpace(_comparePath))
        {
            DivergenceLines = new[] { "Pick a second run's run.json to compare against." };
            return;
        }

        try
        {
            var other = Panopticon.RunOutput.LoadFromFile(_comparePath);
            var report = RunDivergenceComparator.Compare(_timeline.Bundle.Manifest, other!);
            DivergenceLines = FormatReport(report);

            // WI-A5-22: prefer the viewed bundle's captures; fall back to the compared run's.
            var detail = DivergenceDetailInspector.Inspect(report, _timeline.Bundle.Manifest);
            if (detail.State is DivergenceDetailState.Missing or DivergenceDetailState.Unarmed
                && other?.TriggeredCaptures is { Count: > 0 })
            {
                var otherDetail = DivergenceDetailInspector.Inspect(report, other);
                if (otherDetail.State == DivergenceDetailState.Matched)
                {
                    detail = otherDetail;
                }
            }

            DivergenceDetail = detail;
        }
        catch (Exception exception)
        {
            DivergenceLines = new[] { $"Compare failed: {exception.Message}" };
            DivergenceDetail = null;
        }

        await System.Threading.Tasks.Task.CompletedTask;
    }

    internal static IReadOnlyList<string> FormatReport(RunDivergenceReport report)
    {
        if (!report.Comparable)
        {
            return new[] { $"Incompatible: {report.IncompatibilityReason}" };
        }

        if (report.FirstDivergentFrame is not { } frame)
        {
            return new[] { $"No divergence within tolerance ({report.FramesCompared} frames compared)." };
        }

        var lines = new List<string>
        {
            $"First divergence at frame {frame} ({report.Loci.Count} diverged bucket(s)).",
        };
        foreach (var locus in report.Loci.Take(8))
        {
            lines.Add($"  {locus.Path}: {locus.Reason} (0x{locus.ReferenceHash:X8} vs 0x{locus.CurrentHash:X8})");
        }

        if (report.Loci.Count > 8)
        {
            lines.Add($"  ... and {report.Loci.Count - 8} more.");
        }

        return lines;
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(_bundlePath))
        {
            StatusMessage = "Pick a run bundle (directory containing run.json) first.";
            return;
        }

        try
        {
            var bundle = RunBundle.Load(_bundlePath);
            var timeline = new RunBundleTimeline(bundle, Panopticon.SageGame.CncGenerals);
            _playback?.Dispose();
            _timeline?.Dispose();
            Timeline = timeline;
            if (_playbackClock != null)
            {
                Playback = new ResultsPlaybackController(timeline, _playbackClock);
                Playback.StateChanged += (_, _) => ApplyTimelinePosition();
            }
            else
            {
                Playback = null;
            }

            SelectedFrame = timeline.CurrentFrame;
            Series = timeline.Series;
            EventsThroughFrame = timeline.EventsThrough(timeline.CurrentFrame);
            UpdateFrameProjection();
            StatusMessage = DescribeCurrentState();
            DivergenceLines = [];
            DivergenceDetail = null;
            OnChanged(nameof(HasTimeline));
        }
        catch (Exception exception)
        {
            StatusMessage = $"Load failed: {exception.Message}";
        }

        await System.Threading.Tasks.Task.CompletedTask;
    }

    private async System.Threading.Tasks.Task BrowseAsync()
    {
        var picked = await _bundleSelector.SelectBundleAsync(System.Threading.CancellationToken.None);
        if (picked == null)
        {
            return;
        }

        BundlePath = picked;
        await LoadAsync();
    }

    private string DescribeCurrentState() =>
        _timeline?.CurrentState is { } state
            ? $"frame {state.Frame}: {state.Objects.Count} objects, {_timeline.Series.Count} series"
            : "no state loaded";

    /// <summary>WI-A5-31: publishes the timeline's current position to all bound views.</summary>
    private void ApplyTimelinePosition()
    {
        if (_timeline == null)
        {
            return;
        }

        SelectedFrame = _timeline.CurrentFrame;
        EventsThroughFrame = _timeline.EventsThrough(_timeline.CurrentFrame);
        UpdateFrameProjection();
        StatusMessage = DescribeCurrentState();
        OnChanged(nameof(Timeline));
    }

    /// <summary>WI-A5-18: re-projects the recorded frame at the scrub position for rendering.</summary>
    private void UpdateFrameProjection()
    {
        FrameProjection = RecordedStateProjector.Project(
            _timeline?.CurrentState,
            _timeline?.Bundle.Manifest.T1Configuration);
    }

    private void OnChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}
