using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Data;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-M5-06: runs the headless battle over a seed range in one process, one fresh game per
/// seed, and aggregates the per-run reports into win rates with Wilson 95 percent intervals.
/// A failed run is recorded as a failure and the batch continues; draws (no winner) stay in
/// the denominator.
/// </summary>
public static class MassBattleBatchRunner
{
    public const int DefaultRuns = 10;

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var profilePath = string.IsNullOrWhiteSpace(config.MassBattleProfilePath)
            ? Path.Combine(AppContext.BaseDirectory, MassBattleRosterSpawner.DefaultBattleProfileFileName)
            : config.MassBattleProfilePath;
        if (!File.Exists(profilePath))
        {
            reporter.Error($"Battle profile '{profilePath}' does not exist.");
            return 1;
        }

        var variant = ParseVariant(config.MassBattleBatchVariant, reporter);
        if (variant is null)
        {
            return 1;
        }

        var seedStart = config.MassBattleBatchSeedStart ?? 1;
        var runs = config.MassBattleBatchRuns ?? DefaultRuns;
        var reportPath = string.IsNullOrWhiteSpace(config.MassBattleBatchReportPath)
            ? Path.Combine(AppContext.BaseDirectory, "mass-battle-batch-report.json")
            : config.MassBattleBatchReportPath!;
        var runDirectory = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? AppContext.BaseDirectory,
            Path.GetFileNameWithoutExtension(reportPath) + "-runs");
        Directory.CreateDirectory(runDirectory);

        // Variants rewrite side rosters only (WI-M5-07): zones, airfields, and seeds stay the
        // authored ones. The rewritten profile lands in the run directory with an absolute
        // manifest path so it loads standalone.
        var effectiveProfilePath = profilePath;
        if (variant != MassBattleBatchVariant.Battle)
        {
            var profile = MassBattleDeploymentProfile.Load(profilePath);
            // The variant file lands in the run directory; the manifest path must resolve from
            // there, so pin it to the source profile's absolute manifest path.
            var manifestPath = MassBattleRunner.ResolveManifestPathFor(profile, profilePath);
            profile = ApplyVariant(profile with { RosterManifestPath = manifestPath }, variant.Value);
            effectiveProfilePath = Path.Combine(runDirectory, $"variant-{variant.Value.ToString().ToLowerInvariant()}.json");
            File.WriteAllText(
                effectiveProfilePath,
                JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            var rosterSummary = profile.Sides.Select(side => side.Roster).Aggregate((a, b) => $"{a} vs {b}");
            reporter.Info($"Batch variant {variant}: sides field rosters {rosterSummary}.");
        }

        var profileForAggregate = MassBattleDeploymentProfile.Load(effectiveProfilePath);
        var perRun = new List<MassBattleReport>();
        var failures = new List<MassBattleBatchFailure>();
        var wallMinutes = new List<double>();
        for (var i = 0; i < runs; i++)
        {
            var seed = (uint)(seedStart + i);
            var runReportPath = Path.Combine(runDirectory, $"seed-{seed}.json");
            var runConfig = new TestHarnessConfig
            {
                Headless = true,
                Game = config.Game,
                GamePath = config.GamePath,
                MassBattleProfilePath = effectiveProfilePath,
                MassBattleSeed = (int)seed,
                MassBattleFrameLimit = config.MassBattleFrameLimit,
                MassBattleReportPath = runReportPath,
            };

            var exitCode = MassBattleRunner.Run(installation, runConfig, reporter);
            if (exitCode != 0 || !File.Exists(runReportPath))
            {
                failures.Add(new MassBattleBatchFailure
                {
                    Seed = seed,
                    Reason = exitCode == 0
                        ? "no report written"
                        : $"runner exit code {exitCode} (see per-run report if present)",
                });
                continue;
            }

            var report = JsonSerializer.Deserialize<MassBattleReport>(
                File.ReadAllText(runReportPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            perRun.Add(report);

            var benchmarkPath = Path.ChangeExtension(runReportPath, ".benchmark.json");
            if (File.Exists(benchmarkPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(benchmarkPath));
                wallMinutes.Add(document.RootElement.GetProperty("measuredWallMs").GetDouble() / 60000.0);
            }
        }

        var batch = Aggregate(profileForAggregate, effectiveProfilePath, seedStart, runs, perRun, failures, wallMinutes, reportPath);
        MassBattleBatchReportIo.WriteJson(batch, reportPath);
        reporter.Info(MassBattleBatchReportIo.WriteText(batch));
        reporter.Info($"Batch report JSON saved to: {reportPath}");
        reporter.Info($"Per-run reports saved under: {runDirectory}");
        return batch.AllRunsReachedOutcome && failures.Count == 0 ? 0 : 5;
    }

    /// <summary>Rewrites side rosters only: Mirror fields side A's roster on both sides, Swap exchanges them.</summary>
    internal static MassBattleDeploymentProfile ApplyVariant(MassBattleDeploymentProfile profile, MassBattleBatchVariant variant)
    {
        var rosters = profile.Sides.Select(side => side.Roster).ToList();
        return variant switch
        {
            MassBattleBatchVariant.Mirror => profile with
            {
                Sides = profile.Sides
                    .Select(side => side with { Roster = rosters[0] })
                    .ToList(),
            },
            MassBattleBatchVariant.Swap => profile with
            {
                Sides = profile.Sides
                    .Select((side, index) => side with { Roster = rosters[(index + 1) % rosters.Count] })
                    .ToList(),
            },
            _ => profile,
        };
    }

    private static MassBattleBatchVariant? ParseVariant(string? value, ITestHarnessReporter reporter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MassBattleBatchVariant.Battle;
        }

        if (Enum.TryParse<MassBattleBatchVariant>(value, ignoreCase: true, out var variant))
        {
            return variant;
        }

        reporter.Error($"Unknown MassBattleBatchVariant '{value}'; expected Battle, Mirror, or Swap.");
        return null;
    }

    internal static MassBattleBatchReport Aggregate(
        MassBattleDeploymentProfile profile,
        string profilePath,
        int seedStart,
        int runs,
        List<MassBattleReport> perRun,
        List<MassBattleBatchFailure> failures,
        List<double> wallMinutes,
        string batchReportPath)
    {
        var sideIds = profile.Sides.Select(side => side.Id).ToList();
        var sides = new List<MassBattleBatchSideReport>();
        foreach (var sideId in sideIds)
        {
            var wins = perRun.Count(report => report.WinningSideId == sideId);
            var (low, high) = WinRateInterval.Wilson(wins, runs);
            sides.Add(new MassBattleBatchSideReport
            {
                SideId = sideId,
                Wins = wins,
                WinRate = runs > 0 ? (double)wins / runs : 0,
                WilsonLow = low,
                WilsonHigh = high,
            });
        }

        var draws = perRun.Count(report => report.WinningSideId is null);
        var frames = perRun.Select(report => (double)report.FramesRun).ToList();

        return new MassBattleBatchReport
        {
            Map = perRun.FirstOrDefault()?.Map ?? profile.Map.Name,
            ProfilePath = profilePath,
            SeedStart = seedStart,
            Runs = runs,
            Sides = sides,
            Draws = draws,
            Failures = failures.Count,
            FailureDetails = failures,
            MedianFramesToOutcome = (int)Median(frames),
            Frames = Statistics(frames),
            WallMinutes = Statistics(wallMinutes),
            KillsSummed = SumRows(perRun, report => report.Kills),
            KillsPerRunMean = MeanRows(perRun, report => report.Kills),
            LossesSummed = SumRows(perRun, report => report.Losses),
            LossesPerRunMean = MeanRows(perRun, report => report.Losses),
            BatchReportPath = batchReportPath,
            AllRunsReachedOutcome = perRun.Count > 0
                && perRun.All(report => report.OutcomeStatus == nameof(Panopticon.Logic.OutcomeStatus.Decided)),
        };
    }

    private static List<MassBattleKillLossRow> SumRows(
        List<MassBattleReport> perRun,
        Func<MassBattleReport, List<MassBattleKillLossRow>> rows)
    {
        var keys = perRun
            .SelectMany(rows)
            .Select(row => (row.SideId, row.Definition))
            .Distinct()
            .OrderBy(key => key.SideId)
            .ThenBy(key => key.Definition)
            .ToList();
        return keys
            .Select(key => new MassBattleKillLossRow(
                key.SideId,
                key.Definition,
                perRun.Sum(report => rows(report)
                    .Where(row => row.SideId == key.SideId && row.Definition == key.Definition)
                    .Sum(row => row.Count))))
            .ToList();
    }

    private static List<MassBattleKillLossRow> MeanRows(
        List<MassBattleReport> perRun,
        Func<MassBattleReport, List<MassBattleKillLossRow>> rows)
    {
        var summed = SumRows(perRun, rows);
        var count = perRun.Count;
        return summed
            .Select(row => row with { Count = count > 0 ? (int)Math.Round((double)row.Count / count, MidpointRounding.AwayFromZero) : 0 })
            .ToList();
    }

    private static BatchMetricStatistics Statistics(List<double> values) => new()
    {
        Count = values.Count,
        Mean = values.Count > 0 ? values.Average() : 0,
        Median = Median(values),
        SampleVariance = values.Count > 1
            ? values.Sum(value => (value - values.Average()) * (value - values.Average())) / (values.Count - 1)
            : 0,
        Min = values.Count > 0 ? values.Min() : 0,
        Max = values.Count > 0 ? values.Max() : 0,
    };

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToList();
        return sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }
}
