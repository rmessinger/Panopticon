# nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Panopticon.Recording;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.ReplaySketch.ViewModels;

/// <summary>One row of the editable scenario parameter list.</summary>
public sealed record ScenarioParameterEntry(string Key, string Value);

/// <summary>
/// WI-A5-32: editable form over a scenario's scalar, enum, list, and implementation-selection
/// values. Edits are parsed and validated against a candidate clone; only a fully valid edit
/// mutates the wrapped scenario (invalid edits never change it). Domain choice is an
/// implementation selection: the scenario carries no domain-specific fields beyond
/// <c>DomainId</c>, so a switch replaces it without stale values.
/// </summary>
public sealed class ScenarioParametersViewModel : INotifyPropertyChanged
{
    /// <summary>Domains <see cref="HarnessRunner"/> can construct, in canonical order.</summary>
    public static readonly IReadOnlyList<string> KnownDomains = ["rts", "transport-graph"];

    private static readonly string[] DefaultModeTexts = ["(default)", "Full", "Operational"];

    private readonly ReplayScenario _scenario;
    private string _domainText = KnownDomains[0];
    private string _seedText = string.Empty;
    private string _frameCapText = string.Empty;
    private string _recordingModeText = DefaultModeTexts[0];
    private string _sampleIntervalText = string.Empty;
    private string _snapshotIntervalText = string.Empty;
    private ScenarioParameterEntry? _selectedParameter;

    public ScenarioParametersViewModel(ReplayScenario scenario)
    {
        _scenario = scenario;
        Parameters = [];
        ResetFrom(scenario);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> DomainOptions => KnownDomains;

    public IReadOnlyList<string> RecordingModeOptions => DefaultModeTexts;

    public ObservableCollection<ScenarioParameterEntry> Parameters { get; }

    public ScenarioParameterEntry? SelectedParameter
    {
        get => _selectedParameter;
        set
        {
            _selectedParameter = value;
            OnPropertyChanged(nameof(SelectedParameter));
        }
    }

    public string DomainText
    {
        get => _domainText;
        set
        {
            _domainText = value;
            OnPropertyChanged(nameof(DomainText));
        }
    }

    public string SeedText
    {
        get => _seedText;
        set
        {
            _seedText = value;
            OnPropertyChanged(nameof(SeedText));
        }
    }

    public string FrameCapText
    {
        get => _frameCapText;
        set
        {
            _frameCapText = value;
            OnPropertyChanged(nameof(FrameCapText));
        }
    }

    public string RecordingModeText
    {
        get => _recordingModeText;
        set
        {
            _recordingModeText = value;
            OnPropertyChanged(nameof(RecordingModeText));
        }
    }

    public string SampleIntervalText
    {
        get => _sampleIntervalText;
        set
        {
            _sampleIntervalText = value;
            OnPropertyChanged(nameof(SampleIntervalText));
        }
    }

    public string SnapshotIntervalText
    {
        get => _snapshotIntervalText;
        set
        {
            _snapshotIntervalText = value;
            OnPropertyChanged(nameof(SnapshotIntervalText));
        }
    }

    public IReadOnlyList<string> LastErrors { get; private set; } = [];

    /// <summary>Reloads every field from the scenario, discarding unapplied edits.</summary>
    public void ResetFrom(ReplayScenario scenario)
    {
        DomainText = scenario.DomainId;
        SeedText = scenario.Seed?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var cap = scenario.StopConditions.OfType<FrameCapStopCondition>().FirstOrDefault();
        FrameCapText = cap is null ? string.Empty : cap.MaxFrames.ToString(CultureInfo.InvariantCulture);
        RecordingModeText = scenario.Recording?.Mode switch
        {
            RecordingMode.Full => "Full",
            RecordingMode.Operational => "Operational",
            _ => DefaultModeTexts[0],
        };
        SampleIntervalText = scenario.Recording?.SampleInterval?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        SnapshotIntervalText = scenario.Recording?.SnapshotInterval?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        Parameters.Clear();
        foreach (var (key, value) in scenario.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Parameters.Add(new ScenarioParameterEntry(key, value));
        }

        LastErrors = [];
        OnPropertyChanged(nameof(LastErrors));
    }

    public void AddParameter(string key, string value)
    {
        Parameters.Add(new ScenarioParameterEntry(key, value));
    }

    public void RemoveSelected()
    {
        if (SelectedParameter is { } entry)
        {
            Parameters.Remove(entry);
        }
    }

    /// <summary>
    /// Parses and validates the form against a candidate clone. Returns an empty list when the
    /// edit was applied; otherwise returns diagnostics and leaves the scenario untouched.
    /// </summary>
    public IReadOnlyList<string> ApplyChanges()
    {
        var errors = new List<string>();
        var seed = ParseOptionalUInt(SeedText, "seed", errors);
        var frameCap = ParseOptionalUInt(FrameCapText, "frameCap", errors);
        var sampleInterval = ParseOptionalUInt(SampleIntervalText, "recording.sampleInterval", errors);
        var snapshotInterval = ParseOptionalUInt(SnapshotIntervalText, "recording.snapshotInterval", errors);
        var mode = ParseRecordingMode(RecordingModeText, errors);

        var candidate = CloneWith(seed, frameCap, mode, sampleInterval, snapshotInterval);
        var validation = ScenarioValidator.Validate(candidate, null);
        foreach (var diagnostic in validation.Diagnostics)
        {
            errors.Add($"{diagnostic.FieldPath}: {diagnostic.Message}");
        }

        if (errors.Count > 0)
        {
            LastErrors = errors;
            OnPropertyChanged(nameof(LastErrors));
            return LastErrors;
        }

        _scenario.DomainId = candidate.DomainId;
        _scenario.Seed = candidate.Seed;
        _scenario.StopConditions = candidate.StopConditions;
        _scenario.Recording = candidate.Recording;
        _scenario.Parameters = candidate.Parameters;
        LastErrors = [];
        OnPropertyChanged(nameof(LastErrors));
        return LastErrors;
    }

    private ReplayScenario CloneWith(
        uint? seed,
        uint? frameCap,
        RecordingMode? mode,
        uint? sampleInterval,
        uint? snapshotInterval)
    {
        ScenarioRecordingOptions? recording = null;
        if (mode is not null || sampleInterval is not null || snapshotInterval is not null)
        {
            recording = new ScenarioRecordingOptions
            {
                Mode = mode,
                SampleInterval = sampleInterval,
                SnapshotInterval = snapshotInterval,
            };
        }

        return new ReplayScenario
        {
            SchemaVersion = _scenario.SchemaVersion,
            DomainId = DomainText,
            Seed = seed,
            MapPath = _scenario.MapPath,
            BaseRadiusWorldUnits = _scenario.BaseRadiusWorldUnits,
            Placements = [.. _scenario.Placements],
            PopulationGenerators = [.. _scenario.PopulationGenerators],
            CommandGenerators = [.. _scenario.CommandGenerators],
            Parameters = Parameters
                .Where(p => p.Key.Length > 0)
                .GroupBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal),
            StopConditions = frameCap is { } cap ? [new FrameCapStopCondition(cap)] : [.. _scenario.StopConditions],
            RecordedMetricSeries = [.. _scenario.RecordedMetricSeries],
            Recording = recording,
            Players = [.. _scenario.Players],
            Assertions = [.. _scenario.Assertions],
        };
    }

    private static uint? ParseOptionalUInt(string text, string field, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (uint.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        errors.Add($"{field}: '{text.Trim()}' is not a non-negative integer");
        return null;
    }

    private static RecordingMode? ParseRecordingMode(string text, List<string> errors)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed == "(default)")
        {
            return null;
        }

        if (Enum.TryParse<RecordingMode>(trimmed, ignoreCase: true, out var mode))
        {
            return mode;
        }

        errors.Add($"recording.mode: '{trimmed}' is not one of: (default), Full, Operational");
        return null;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
