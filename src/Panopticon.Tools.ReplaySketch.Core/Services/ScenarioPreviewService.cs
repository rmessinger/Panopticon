#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Panopticon.Data;
using Panopticon.Recording;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>Projected state at one preview frame: entity count and the region buckets they occupy.</summary>
public sealed record ScenarioPreviewProjection(uint Frame, int EntityCount, IReadOnlyList<string> RegionKeys);

/// <summary>
/// Outcome of a bounded preview run. `Ran` is false when validation failed or no installation was
/// available, in which case no host was constructed (WI-A5-13).
/// </summary>
public sealed record ScenarioPreviewResult(
    bool Ran,
    uint FramesCompleted,
    bool HitFrameBudget,
    bool ExceededTimeBudget,
    long WallClockMilliseconds,
    ScenarioPreviewProjection? FrameZero,
    ScenarioPreviewProjection? Final,
    IReadOnlyList<string> Diagnostics)
{
    public static ScenarioPreviewResult NotRun(IReadOnlyList<string> diagnostics) =>
        new(false, 0, false, false, 0, null, null, diagnostics);
}

/// <summary>
/// WI-A5-13: validates a scenario, runs it for a strict frame budget through the same harness the
/// replay path uses, and projects frame zero plus the final frame through the same domain
/// projection the Results view uses. No host is constructed for invalid scenarios or when the
/// installation cannot be resolved.
/// </summary>
public static class ScenarioPreviewService
{
    public const int DefaultFrameBudget = 60;
    public const int DefaultTimeBudgetMs = 30_000;

    public static ScenarioPreviewResult Preview(
        ReplayScenario scenario,
        GameInstallation? installation,
        string tempDirectory,
        int frameBudget = DefaultFrameBudget,
        int timeBudgetMs = DefaultTimeBudgetMs)
    {
        // Structural validation first: an invalid scenario must never reach an installation or
        // host (WI-A5-13). Map-dependent checks rerun below once the map is loaded.
        var structural = ScenarioValidator.Validate(scenario, null);
        if (!structural.IsValid)
        {
            return ScenarioPreviewResult.NotRun([.. structural.Diagnostics.Select(d => $"{d.FieldPath}: {d.Message}")]);
        }

        if (installation is null)
        {
            return ScenarioPreviewResult.NotRun(["no installation resolved; preview needs a game install"]);
        }

        MapMetadataService? map = null;
        try
        {
            map = MapMetadataService.Load(installation, scenario.MapPath);
        }
        catch (Exception ex)
        {
            return ScenarioPreviewResult.NotRun([$"map load failed: {ex.Message}"]);
        }

        var validation = ScenarioValidator.Validate(scenario, map);
        if (!validation.IsValid)
        {
            return ScenarioPreviewResult.NotRun(validation.Diagnostics.Select(d => $"{d.FieldPath}: {d.Message}").ToArray());
        }

        var budget = Math.Max(1u, (uint)frameBudget);
        var previewScenario = CloneForPreview(scenario, budget);
        var bundleDirectory = Path.Combine(tempDirectory, "preview-bundle");
        Directory.CreateDirectory(bundleDirectory);

        var stopwatch = Stopwatch.StartNew();
        Panopticon.RunOutput? output = null;
        HarnessRunner.RunAndAssert(
            previewScenario,
            installation,
            tempDirectory,
            onRunCompleted: loaded => output = loaded,
            bundleDirectory: bundleDirectory);
        stopwatch.Stop();

        var contents = RunBundle.Load(bundleDirectory);
        using var reader = new WorldStateArtifactReader(contents.StateArtifactPath, SageGame.CncGenerals);

        var diagnostics = new List<string>();
        if (reader.IsTruncated && reader.TruncationDiagnostic is { } truncation)
        {
            diagnostics.Add(truncation);
        }

        var completed = output?.TotalLogicFrames ?? reader.LastFrame;
        var hitFrameBudget = completed >= budget;
        if (hitFrameBudget)
        {
            diagnostics.Add($"frame budget reached ({budget} frames)");
        }

        var exceededTimeBudget = stopwatch.ElapsedMilliseconds > timeBudgetMs;
        if (exceededTimeBudget)
        {
            diagnostics.Add($"exceeded time budget: {stopwatch.ElapsedMilliseconds} ms > {timeBudgetMs} ms");
        }

        return new ScenarioPreviewResult(
            Ran: true,
            FramesCompleted: completed,
            HitFrameBudget: hitFrameBudget,
            ExceededTimeBudget: exceededTimeBudget,
            WallClockMilliseconds: stopwatch.ElapsedMilliseconds,
            FrameZero: ProjectAt(reader, contents, 0),
            Final: ProjectAt(reader, contents, Math.Min(budget, reader.LastFrame)),
            Diagnostics: diagnostics);
    }

    /// <summary>
    /// WI-A5-13: single-line-plus-detail summary for the scenario panel. Lives in Core so tests
    /// and any future front-end share one format.
    /// </summary>
    public static string FormatSummary(ScenarioPreviewResult result)
    {
        if (!result.Ran)
        {
            return $"Preview not run: {string.Join("; ", result.Diagnostics)}";
        }

        var lines = new List<string>
        {
            $"Preview: {result.FramesCompleted} frames in {result.WallClockMilliseconds} ms",
            $"Frame 0: {result.FrameZero?.EntityCount ?? 0} entities",
            $"Final: {result.Final?.EntityCount ?? 0} entities in {result.Final?.RegionKeys.Count ?? 0} region(s)",
        };
        lines.AddRange(result.Diagnostics);
        return string.Join(Environment.NewLine, lines);
    }

    private static ScenarioPreviewProjection? ProjectAt(WorldStateArtifactReader reader, RunBundleContents contents, uint frame)
    {
        var summary = reader.ReconstructStateAt(frame);
        var projection = RecordedStateProjector.Project(summary, contents.Manifest.T1Configuration);
        return new ScenarioPreviewProjection(frame, projection.Points.Count, projection.Points.Select(p => p.Point.BucketKey).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    private static ReplayScenario CloneForPreview(ReplayScenario scenario, uint frameBudget)
    {
        return new ReplayScenario
        {
            SchemaVersion = scenario.SchemaVersion,
            DomainId = scenario.DomainId,
            Seed = scenario.Seed,
            MapPath = scenario.MapPath,
            BaseRadiusWorldUnits = scenario.BaseRadiusWorldUnits,
            Placements = scenario.Placements.ToList(),
            Parameters = new Dictionary<string, string>(scenario.Parameters, StringComparer.Ordinal),
            StopConditions = [new FrameCapStopCondition(frameBudget)],
            RecordedMetricSeries = scenario.RecordedMetricSeries.ToList(),
            Recording = new ScenarioRecordingOptions
            {
                Mode = scenario.Recording?.Mode,
                SampleInterval = scenario.Recording?.SampleInterval ?? 15,
                SnapshotInterval = scenario.Recording?.SnapshotInterval ?? 300,
            },
            PopulationGenerators = [],
            CommandGenerators = [],
            Players = scenario.Players
                .Select(p => new PlayerSlotConfig
                {
                    Name = p.Name,
                    FactionIndex = p.FactionIndex,
                    Color = p.Color,
                    StartPosition = p.StartPosition,
                    Team = p.Team,
                    StartingMoney = p.StartingMoney,
                    Actions = p.Actions.ToList(),
                })
                .ToList(),
            Assertions = scenario.Assertions.ToList(),
        };
    }
}
