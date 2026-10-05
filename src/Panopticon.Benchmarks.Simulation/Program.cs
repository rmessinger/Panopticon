using System;
using System.IO;
using System.Text.Json;

namespace Panopticon.Benchmarks.Simulation;

public static class Program
{
    public static int Main(string[] args)
    {
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

    private static void WriteReport(BenchmarkRunRecord record, SimulationBenchmarkPlan plan)
    {
        if (plan.JsonPath is not null)
        {
            var json = JsonSerializer.Serialize(
                record,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            File.WriteAllText(plan.JsonPath, json);
        }

        Console.WriteLine($"scenario        {record.ScenarioId}");
        Console.WriteLine($"seed            {record.Seed}");
        Console.WriteLine($"frames          {record.FramesCompleted}/{record.MeasuredFrames} completed");
        Console.WriteLine($"measured wall   {record.MeasuredWallMs:F1} ms");
        Console.WriteLine($"json report     {plan.JsonPath ?? "(not written; pass --json)"}");
        Console.WriteLine($"csv summary     {plan.CsvPath ?? "(not written; pass --csv)"}");
        if (plan.CsvPath is not null)
        {
            Console.WriteLine("csv writing arrives in B1-4; path accepted and stored");
        }
    }
}
