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
    private RunBundleTimeline? _timeline;
    private string? _bundlePath;
    private string? _comparePath;
    private string _statusMessage = string.Empty;
    private uint _selectedFrame;
    private IReadOnlyList<RunEventRecord> _eventsThroughFrame = [];
    private IReadOnlyList<MetricSeriesRecord> _series = [];
    private IReadOnlyList<string> _divergenceLines = [];

    public ResultsViewModel(IBundleFileSelector bundleSelector)
    {
        _bundleSelector = bundleSelector ?? throw new ArgumentNullException(nameof(bundleSelector));
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

    /// <summary>WI-A5-05: compares the loaded bundle's T1 trees against a second run manifest.</summary>
    public AsyncCommand CompareCommand { get; }

    public void ScrubTo(uint frame)
    {
        if (_timeline == null)
        {
            return;
        }

        _timeline.SeekTo(frame);
        SelectedFrame = _timeline.CurrentFrame;
        EventsThroughFrame = _timeline.EventsThrough(_timeline.CurrentFrame);
        StatusMessage = DescribeCurrentState();
        OnChanged(nameof(Timeline));
    }

    public void Dispose()
    {
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
        }
        catch (Exception exception)
        {
            DivergenceLines = new[] { $"Compare failed: {exception.Message}" };
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
            _timeline?.Dispose();
            Timeline = timeline;
            SelectedFrame = timeline.CurrentFrame;
            Series = timeline.Series;
            EventsThroughFrame = timeline.EventsThrough(timeline.CurrentFrame);
            StatusMessage = DescribeCurrentState();
            DivergenceLines = [];
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

    private void OnChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}
