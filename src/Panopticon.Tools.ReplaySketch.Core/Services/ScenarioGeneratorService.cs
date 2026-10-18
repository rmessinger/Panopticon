#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// WI-A5-23: expands declarative generators into concrete placements and command timelines.
/// Deterministic: scattered positions come from one <see cref="Random"/> seeded by the
/// declarative scenario seed (absence means 0), consumed in generator order. Generator bounds
/// are validated before any output is produced, and the expanded scenario is itself validated
/// (without a map) before it is returned.
/// </summary>
public static class ScenarioGeneratorService
{
    public static ReplayScenario Expand(ReplayScenario scenario)
    {
        ValidateGeneratorBounds(scenario);

        var rng = new Random(unchecked((int)(scenario.Seed ?? 0)));
        var expanded = CloneWithoutGenerators(scenario);

        foreach (var generator in scenario.PopulationGenerators)
        {
            expanded.Placements.AddRange(ExpandPopulation(generator, rng));
        }

        foreach (var generator in scenario.CommandGenerators)
        {
            ExpandCommands(expanded, generator);
        }

        var report = ScenarioValidator.Validate(expanded);
        if (!report.IsValid)
        {
            throw new InvalidOperationException(
                "Expanded scenario is invalid: " + Describe(report.Diagnostics));
        }

        return expanded;
    }

    private static void ValidateGeneratorBounds(ReplayScenario scenario)
    {
        var errors = new List<string>();
        for (var i = 0; i < scenario.PopulationGenerators.Count; i++)
        {
            var generator = scenario.PopulationGenerators[i];
            if (generator == null)
            {
                errors.Add($"populationGenerators[{i}]: generator is null");
                continue;
            }

            if (string.IsNullOrWhiteSpace(generator.DefinitionName))
            {
                errors.Add($"populationGenerators[{i}]: definition name is empty");
            }

            if (generator is FormationPopulationGenerator formation && formation.Quantity < 1)
            {
                errors.Add($"populationGenerators[{i}]: quantity must be at least 1");
            }

            if (generator is GridPopulationGenerator grid && (grid.Rows == 0 || grid.Columns == 0))
            {
                errors.Add($"populationGenerators[{i}]: grid rows and columns must be at least 1");
            }

            if (generator is ScatteredPopulationGenerator scattered && (scattered.Quantity < 1 || scattered.Radius <= 0))
            {
                errors.Add($"populationGenerators[{i}]: scattered radius must be positive");
            }
        }

        for (var i = 0; i < scenario.CommandGenerators.Count; i++)
        {
            var generator = scenario.CommandGenerators[i];
            if (generator == null)
            {
                errors.Add($"commandGenerators[{i}]: generator is null");
                continue;
            }

            if (generator.Template.Count == 0)
            {
                errors.Add($"commandGenerators[{i}]: template is empty");
            }

            if (generator.RepeatCount == 0)
            {
                errors.Add($"commandGenerators[{i}]: repeat count must be at least 1");
            }

            for (var t = 0; t < generator.Template.Count; t++)
            {
                if (generator.Template[t] == null)
                {
                    errors.Add($"commandGenerators[{i}].template[{t}]: action is null");
                }
                else if (generator.Template[t].Timing is not FixedTiming)
                {
                    errors.Add($"commandGenerators[{i}].template[{t}]: timing must be fixed for generated timelines");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid generator bounds: " + string.Join("; ", errors));
        }
    }

    private static IEnumerable<ScenarioPlacement> ExpandPopulation(PopulationGenerator generator, Random rng)
    {
        switch (generator)
        {
            case GridPopulationGenerator grid:
            {
                for (var row = 0; row < grid.Rows; row++)
                {
                    for (var column = 0; column < grid.Columns; column++)
                    {
                        var x = grid.OriginX + (column - (grid.Columns - 1) / 2f) * grid.SpacingX;
                        var y = grid.OriginY + (row - (grid.Rows - 1) / 2f) * grid.SpacingY;
                        yield return Placement(generator, x, y);
                    }
                }

                break;
            }

            case FormationPopulationGenerator formation:
            {
                var columns = formation.Shape switch
                {
                    FormationShape.Line => formation.Quantity,
                    FormationShape.Column => 1u,
                    FormationShape.Box => (uint)Math.Ceiling(Math.Sqrt(formation.Quantity)),
                    _ => formation.Quantity,
                };
                for (var i = 0; i < formation.Quantity; i++)
                {
                    var column = i % columns;
                    var row = i / columns;
                    var x = formation.OriginX + (column - (columns - 1) / 2f) * formation.Spacing;
                    var y = formation.OriginY - row * formation.Spacing;
                    yield return Placement(generator, x, y);
                }

                break;
            }

            case ScatteredPopulationGenerator scattered:
            {
                for (var i = 0; i < scattered.Quantity; i++)
                {
                    // Uniform in the circle: radius scaled by sqrt(sample).
                    var radius = scattered.Radius * Math.Sqrt(rng.NextDouble());
                    var angle = 2 * Math.PI * rng.NextDouble();
                    yield return Placement(generator, scattered.CenterX + (float)(radius * Math.Cos(angle)), scattered.CenterY + (float)(radius * Math.Sin(angle)));
                }

                break;
            }

            default:
                throw new InvalidOperationException($"Unknown population generator kind '{generator.GetType().Name}'.");
        }
    }

    private static ScenarioPlacement Placement(PopulationGenerator generator, float x, float y) =>
        new()
        {
            DefinitionName = generator.DefinitionName,
            PlayerIndex = generator.PlayerIndex,
            Position = new NormalizedPosition(x, y),
            Quantity = 1,
            Health = generator.Health,
            MaxHealth = generator.MaxHealth,
        };

    private static void ExpandCommands(ReplayScenario expanded, CommandTimelineGenerator generator)
    {
        var player = expanded.Players[generator.PlayerIndex];
        var cumulative = player.Actions.Sum(action => action.Timing is FixedTiming fixedTiming ? fixedTiming.Frames : 0u);

        for (var repetition = 0; repetition < generator.RepeatCount; repetition++)
        {
            var repetitionStart = generator.StartFrame + (uint)repetition * generator.IntervalFrames;
            var offsetInRepetition = 0u;
            for (var t = 0; t < generator.Template.Count; t++)
            {
                var template = generator.Template[t];
                var targetFrame = repetitionStart + offsetInRepetition;
                if (targetFrame < cumulative)
                {
                    throw new InvalidOperationException(
                        $"Command generator repetitions overlap: action {t} of repetition {repetition} targets frame {targetFrame} but the schedule is already at {cumulative}. Increase intervalFrames.");
                }

                var delta = checked((uint)(targetFrame - cumulative));
                player.Actions.Add(new ActionEntry(
                    $"{template.Label}-r{repetition}",
                    template.Command,
                    template.Position,
                    new FixedTiming(delta),
                    id: $"{template.Id ?? "action"}-g{repetition}t{t}",
                    buildingDefinitionName: template.BuildingDefinitionName,
                    unitDefinitionName: template.UnitDefinitionName,
                    type: template.Type,
                    quantity: template.Quantity));
                cumulative = targetFrame;
                offsetInRepetition += template.Timing is FixedTiming fixedTiming ? fixedTiming.Frames : 0u;
            }
        }
    }

    private static ReplayScenario CloneWithoutGenerators(ReplayScenario scenario) => new()
    {
        SchemaVersion = scenario.SchemaVersion,
        DomainId = scenario.DomainId,
        Seed = scenario.Seed,
        MapPath = scenario.MapPath,
        BaseRadiusWorldUnits = scenario.BaseRadiusWorldUnits,
        Placements = [.. scenario.Placements],
        Parameters = new Dictionary<string, string>(scenario.Parameters, StringComparer.Ordinal),
        StopConditions = [.. scenario.StopConditions],
        RecordedMetricSeries = [.. scenario.RecordedMetricSeries],
        Recording = scenario.Recording,
        Players = scenario.Players
            .Select(player => new PlayerSlotConfig
            {
                Name = player.Name,
                FactionIndex = player.FactionIndex,
                Color = player.Color,
                StartPosition = player.StartPosition,
                Team = player.Team,
                StartingMoney = player.StartingMoney,
                Actions = [.. player.Actions],
            })
            .ToList(),
        Assertions = [.. scenario.Assertions],
    };

    private static string Describe(IEnumerable<ScenarioValidationDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Take(10).Select(d => $"{d.FieldPath}: {d.Message}"));
}
