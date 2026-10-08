using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Benchmarks.Simulation.Experiments;

namespace Panopticon.Benchmarks.Simulation;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--experiment")
        {
            return RunExperiment(args);
        }

        SimulationBenchmarkPlan? plan;
        try
        {
            plan = SimulationBenchmarkCommandLine.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(SimulationBenchmarkCommandLine.Usage);
            return 2;
        }

        if (plan is null)
        {
            Console.Out.WriteLine(SimulationBenchmarkCommandLine.Usage);
            return 0;
        }

        var runner = new SimulationBenchmarkRunner();
        try
        {
            var record = runner.Run(plan);
            WriteReport(record, plan);
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Experiment mode (WI-A4-41): `--experiment <definition.json> [--out <directory>]` runs
    /// every cell of an experiment definition through the benchmark runner and writes one
    /// comparable report. Exit codes follow the benchmark contract: 0 when every cell
    /// succeeds, 1 on run or report failure, 2 on argument errors.
    /// </summary>
    private static int RunExperiment(string[] args)
    {
        string definitionPath;
        string? outputDirectory = null;
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Option '--experiment' requires a definition path.");
            }

            definitionPath = args[1];
            for (var i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out":
                        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new ArgumentException("Option '--out' requires a value.");
                        }

                        outputDirectory = args[++i];
                        break;
                    default:
                        throw new ArgumentException($"Unknown option '{args[i]}'.");
                }
            }

            var definition = ExperimentDefinition.FromJson(File.ReadAllText(definitionPath));
            var execution = new ExperimentRunner(ExperimentImplementationRegistry.CreateDefault()).Run(definition);
            var directory = outputDirectory
                ?? Path.Combine(
                    "experiments",
                    definition.Name,
                    DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
            var reportPath = ExperimentReportWriter.Write(execution, directory);

            foreach (var cell in execution.Cells)
            {
                Console.WriteLine($"cell            {cell.CellKey} {(cell.Succeeded ? "ok" : $"failed: {cell.Error}")}");
            }

            Console.WriteLine($"cells           {execution.Cells.Count(c => c.Succeeded)}/{execution.Cells.Count} succeeded");
            Console.WriteLine($"report          {reportPath}");
            return execution.AllCellsSucceeded ? 0 : 1;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: Panopticon.Benchmarks.Simulation --experiment <definition.json> [--out <directory>]");
            return 2;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void WriteReport(BenchmarkRunRecord record, SimulationBenchmarkPlan plan)
    {
        if (plan.JsonPath is not null)
        {
            BenchmarkReportWriter.WriteJson(record, plan.JsonPath);
        }

        if (plan.CsvPath is not null)
        {
            BenchmarkReportWriter.WriteCsv(record, plan.CsvPath);
        }

        Console.WriteLine($"scenario        {record.ScenarioId}");
        Console.WriteLine($"seed            {record.Seed}");
        Console.WriteLine($"frames          {record.FramesCompleted}/{record.MeasuredFrames} completed");
        Console.WriteLine($"measured wall   {record.MeasuredWallMs:F1} ms");
        var regressions = record.Statistics.Count(s => s.IsRegression == true);
        if (record.Statistics.Any(s => s.IsRegression.HasValue))
        {
            Console.WriteLine($"vs baseline     {regressions} metric(s) changed beyond CI overlap");
        }

        Console.WriteLine($"json report     {plan.JsonPath ?? "(not written; pass --json)"}");
        Console.WriteLine($"csv summary     {plan.CsvPath ?? "(not written; pass --csv)"}");
    }
}
