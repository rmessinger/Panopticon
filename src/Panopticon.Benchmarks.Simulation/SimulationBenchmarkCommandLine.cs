using System;
using System.Collections.Generic;
using System.Globalization;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Parses the benchmark CLI into a plan. Pure argument handling: no file, install, or game
/// access, so plan construction works without an installation.
/// </summary>
public static class SimulationBenchmarkCommandLine
{
    public const string DefaultScenarioId = EmptyHostBenchmarkScenario.ScenarioId;

    public const uint DefaultSeed = 1;

    public const int DefaultWarmupFrames = 30;

    public const int DefaultMeasuredFrames = 300;

    public static string Usage => """
        Usage: Panopticon.Benchmarks.Simulation [options]

        Options:
          --scenario <id>         Scenario id (default b1.empty-host)
          --seed <n>              Seed forwarded to the game start (default 1)
          --warmup-frames <n>     Frames stepped before measurement (default 30)
          --measured-frames <n>   Frames stepped under the recorder (default 300)
          --map <path>            Map path override; scenario default otherwise
          --json <path>           JSON run record destination
          --csv <path>            CSV summary destination (written from B1-4 on)
          --baseline <path>       Baseline JSON record for regression comparison
          --help                  Print this usage
        """;

    /// <summary>Returns the parsed plan, or null when --help was requested.</summary>
    public static SimulationBenchmarkPlan? Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? scenarioId = null;
        string? mapPath = null;
        string? jsonPath = null;
        string? csvPath = null;
        string? baselinePath = null;
        uint seed = DefaultSeed;
        int warmupFrames = DefaultWarmupFrames;
        int measuredFrames = DefaultMeasuredFrames;

        var items = new Queue<string>(args);
        while (items.Count > 0)
        {
            var option = items.Dequeue();
            if (option == "--help")
            {
                return null;
            }

            var value = NextValue(items, option);
            switch (option)
            {
                case "--scenario":
                    scenarioId = value;
                    break;
                case "--seed":
                    seed = ParseUInt(option, value);
                    break;
                case "--warmup-frames":
                    warmupFrames = ParseInt(option, value);
                    break;
                case "--measured-frames":
                    measuredFrames = ParseInt(option, value);
                    break;
                case "--map":
                    mapPath = value;
                    break;
                case "--json":
                    jsonPath = value;
                    break;
                case "--csv":
                    csvPath = value;
                    break;
                case "--baseline":
                    baselinePath = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        try
        {
            return new SimulationBenchmarkPlan(scenarioId ?? DefaultScenarioId, seed, warmupFrames, measuredFrames)
            {
                MapPath = mapPath,
                JsonPath = jsonPath,
                CsvPath = csvPath,
                BaselinePath = baselinePath,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            throw new ArgumentException($"Invalid plan: {ex.Message}", ex);
        }
    }

    private static string NextValue(Queue<string> items, string option)
    {
        if (items.Count == 0 || items.Peek().StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Option '{option}' requires a value.");
        }

        return items.Dequeue();
    }

    private static uint ParseUInt(string option, string value)
    {
        if (!uint.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"Option '{option}' expects an unsigned integer, got '{value}'.");
        }

        return parsed;
    }

    private static int ParseInt(string option, string value)
    {
        if (!int.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"Option '{option}' expects an integer, got '{value}'.");
        }

        return parsed;
    }
}
