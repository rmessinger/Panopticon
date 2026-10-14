using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Units.Ee2;
using Panopticon.IO;
using Panopticon.Logic;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Card WI-M3-03: prices the committed roster manifest through the recorded cross-game rates
/// (Generals BuildCost at 1 point per dollar, EE2 resource columns at the authored rate per
/// resource unit) and reports per-entry and total points, unit counts, anti-air and aircraft
/// presence, and the tolerance verdict. Generals values come from the booted game's asset
/// store; EE2 values from the install's tech-tree table.
/// </summary>
public static class RosterBudgetEvaluator
{
    public const string DefaultManifestFileName = "mass-battle-rosters.json";

    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static RosterManifest LoadDefault()
    {
        return Load(Path.Combine(AppContext.BaseDirectory, DefaultManifestFileName));
    }

    public static RosterManifest Load(string path)
    {
        var manifest = JsonSerializer.Deserialize<RosterManifest>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException($"Could not load roster manifest '{path}'.");
        return manifest;
    }

    public static RosterBudgetReport Evaluate(RosterManifest manifest, Game game)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(game);

        var sides = manifest.Sides.Select(side => EvaluateSide(manifest, side, game)).ToList();
        var larger = sides.Max(s => s.Points);
        var difference = sides.Max(s => s.Points) - sides.Min(s => s.Points);
        var differencePercent = larger > 0 ? difference / larger * 100.0 : 0;

        return new RosterBudgetReport
        {
            Manifest = manifest.Name,
            Sides = sides,
            TolerancePercent = manifest.TolerancePercent,
            PointsDifference = difference,
            DifferencePercent = Math.Round(differencePercent, 4, MidpointRounding.AwayFromZero),
            WithinTolerance = differencePercent <= manifest.TolerancePercent,
        };
    }

    private static RosterSideReport EvaluateSide(RosterManifest manifest, RosterSide side, Game game)
    {
        var entries = side.Entries.Select(entry => EvaluateEntry(manifest, entry, game)).ToList();
        return new RosterSideReport
        {
            Id = side.Id,
            Units = entries.Sum(e => e.Count),
            Points = entries.Sum(e => e.TotalPoints),
            Entries = entries,
            AntiAirPresent = entries.Any(e => e.AntiAir),
            HasAircraft = entries.Any(e => e.Aircraft),
        };
    }

    private static RosterEntryReport EvaluateEntry(RosterManifest manifest, RosterEntry entry, Game game)
    {
        double unitPoints;
        double resourceSum = 0;
        if (entry.Source == "generals")
        {
            var definition = game.AssetStore.ObjectDefinitions.GetByName(entry.Definition)
                ?? throw new InvalidOperationException($"Generals definition '{entry.Definition}' was not found; the manifest prices real BuildCosts.");
            unitPoints = definition.BuildCost * manifest.PointsRates.GeneralsPointsPerBuildCostDollar;
        }
        else if (entry.Source == "ee2")
        {
            resourceSum = Ee2ResourceSum(entry.Definition);
            unitPoints = resourceSum * manifest.PointsRates.Ee2PointsPerResourceUnit;
        }
        else
        {
            throw new InvalidOperationException($"Roster entry '{entry.Definition}' names unknown source '{entry.Source}'.");
        }

        return new RosterEntryReport
        {
            Definition = entry.Definition,
            Source = entry.Source,
            Count = entry.Count,
            ResourceSum = entry.Source == "ee2" ? resourceSum : null,
            UnitPoints = unitPoints,
            TotalPoints = unitPoints * entry.Count,
            AntiAir = entry.AntiAir,
            Aircraft = entry.Aircraft,
        };
    }

    /// <summary>Summed cost columns of one exact tech-tree row key, read from the operator install.</summary>
    public static double Ee2ResourceSum(string rowKey)
    {
        var root = Environment.GetEnvironmentVariable(UnitContentImportRunner.Ee2PathVariable)
            ?? throw new InvalidOperationException($"Unmet prerequisite: {UnitContentImportRunner.Ee2PathVariable} must point at an Empire Earth II install.");

        using var database = new ZipFileSystem(Path.Combine(root, "zips/db.zip"));
        var entry = database.GetFile(Ee2SourceFiles.DefaultUnitTablePath)
            ?? throw new InvalidOperationException($"Unit table '{Ee2SourceFiles.DefaultUnitTablePath}' was not found in the install.");

        using var reader = new StreamReader(entry.Open());
        var table = Ee2CsvTable.Read(reader.ReadToEnd(), Ee2SourceFiles.DefaultUnitTablePath);
        if (!table.TryGetRow(rowKey, out var row, out _))
        {
            throw new InvalidOperationException($"No '{rowKey}' row in {Ee2SourceFiles.DefaultUnitTablePath}.");
        }

        return Ee2SourceFiles.CostColumns
            .Where(column => row.TryGetValue(column, out var cell) && double.TryParse(cell, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            .Sum(column => double.Parse(row[column], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>The committed manifest: recorded rates, tolerance, and the two roster manifests.</summary>
public sealed class RosterManifest
{
    public string Name { get; set; } = "";

    public RosterPointsRates PointsRates { get; set; } = new();

    /// <summary>Authored tolerance as a percentage of the larger side total.</summary>
    public double TolerancePercent { get; set; }

    public List<RosterSide> Sides { get; set; } = [];
}

public sealed class RosterPointsRates
{
    /// <summary>Points per Generals BuildCost dollar.</summary>
    public double GeneralsPointsPerBuildCostDollar { get; set; } = 1.0;

    /// <summary>Points per summed EE2 resource unit.</summary>
    public double Ee2PointsPerResourceUnit { get; set; } = 1.0;
}

public sealed class RosterSide
{
    public string Id { get; set; } = "";

    public List<RosterEntry> Entries { get; set; } = [];
}

public sealed class RosterEntry
{
    /// <summary>Definition name: a native ObjectDefinition for generals rows, an exact tech-tree row key for ee2 rows.</summary>
    public string Definition { get; set; } = "";

    public string Source { get; set; } = "generals";

    public int Count { get; set; }

    /// <summary>Authored role marker: the entry fields units that can engage air targets.</summary>
    public bool AntiAir { get; set; }

    /// <summary>Authored role marker: the entry fields aircraft (aircraft count toward the 1000).</summary>
    public bool Aircraft { get; set; }
}

/// <summary>Machine-readable budget report.</summary>
public sealed record RosterBudgetReport
{
    public string Manifest { get; init; } = "";

    public List<RosterSideReport> Sides { get; init; } = [];

    public double TolerancePercent { get; init; }

    public double PointsDifference { get; init; }

    public double DifferencePercent { get; init; }

    public bool WithinTolerance { get; init; }
}

public sealed record RosterSideReport
{
    public string Id { get; init; } = "";

    public int Units { get; init; }

    public double Points { get; init; }

    public List<RosterEntryReport> Entries { get; init; } = [];

    public bool AntiAirPresent { get; init; }

    public bool HasAircraft { get; init; }
}

public sealed record RosterEntryReport
{
    public string Definition { get; init; } = "";

    public string Source { get; init; } = "";

    public int Count { get; init; }

    /// <summary>Summed EE2 resource columns before the rate (null for Generals rows).</summary>
    public double? ResourceSum { get; init; }

    public double UnitPoints { get; init; }

    public double TotalPoints { get; init; }

    public bool AntiAir { get; init; }

    public bool Aircraft { get; init; }
}
