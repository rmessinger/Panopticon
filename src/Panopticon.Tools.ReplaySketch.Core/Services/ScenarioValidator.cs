#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// One validation finding: a JSON-style field path into the scenario artifact plus the rule that
/// failed. WI-A5-10.
/// </summary>
public sealed record ScenarioValidationDiagnostic(string FieldPath, string Message);

/// <summary>
/// Every independent finding from one validation pass, in stable (field order) order. An empty
/// list means the scenario may execute. WI-A5-10.
/// </summary>
public sealed class ScenarioValidationReport
{
    public IReadOnlyList<ScenarioValidationDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.Count == 0;

    public ScenarioValidationReport(IReadOnlyList<ScenarioValidationDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics;
    }
}

/// <summary>
/// WI-A5-10: semantic validation of the complete versioned scenario before host construction.
/// Structural load-time rejection stays in <see cref="ReplayScenarioLoader"/>; this validator
/// walks every field group (schema, map, players, actions, placements, parameters, stop
/// conditions, metric series, recording options, generators) and reports all independent errors
/// with their artifact field paths. When a <see cref="MapMetadataService"/> is supplied, action
/// positions are also resolved and extent-checked (the TerrainValidator rules).
/// </summary>
public static class ScenarioValidator
{
    public static ScenarioValidationReport Validate(ReplayScenario scenario, MapMetadataService? map = null)
    {
        var diagnostics = new List<ScenarioValidationDiagnostic>();

        ValidateHeader(scenario, diagnostics);
        ValidatePlayers(scenario, diagnostics);
        ValidatePlacements(scenario, diagnostics);
        ValidateParameters(scenario, diagnostics);
        ValidateStopConditions(scenario, diagnostics);
        ValidateRecordedMetricSeries(scenario, diagnostics);
        ValidateRecordingOptions(scenario, diagnostics);
        ValidateGenerators(scenario, diagnostics);
        if (map != null)
        {
            ValidateActionPositions(scenario, map, diagnostics);
        }

        return new ScenarioValidationReport(diagnostics);
    }

    private static void ValidateHeader(ReplayScenario scenario, List<ScenarioValidationDiagnostic> diagnostics)
    {
        if (scenario.SchemaVersion != ReplayScenario.CurrentSchemaVersion)
        {
            diagnostics.Add(new ScenarioValidationDiagnostic(
                "schemaVersion",
                $"Schema version {scenario.SchemaVersion} is not the understood version {ReplayScenario.CurrentSchemaVersion}."));
        }

        if (string.IsNullOrWhiteSpace(scenario.MapPath))
        {
            diagnostics.Add(new ScenarioValidationDiagnostic("mapPath", "Map path is empty."));
        }

        if (string.IsNullOrWhiteSpace(scenario.DomainId))
        {
            diagnostics.Add(new ScenarioValidationDiagnostic("domainId", "Domain id is empty."));
        }
    }

    private static void ValidatePlayers(ReplayScenario scenario, List<ScenarioValidationDiagnostic> diagnostics)
    {
        if (scenario.Players.Count == 0)
        {
            diagnostics.Add(new ScenarioValidationDiagnostic("players", "Scenario has no players."));
            return;
        }

        var actionIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < scenario.Players.Count; i++)
        {
            var player = scenario.Players[i];
            if (string.IsNullOrWhiteSpace(player.Name))
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"players[{i}].name", "Player name is empty."));
            }

            if (player.FactionIndex < 0)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"players[{i}].factionIndex", "Faction index is negative."));
            }

            for (var j = 0; j < player.Actions.Count; j++)
            {
                var action = player.Actions[j];
                if (action == null)
                {
                    diagnostics.Add(new ScenarioValidationDiagnostic($"players[{i}].actions[{j}]", "Action entry is null."));
                    continue;
                }

                if (action.Command == null)
                {
                    diagnostics.Add(new ScenarioValidationDiagnostic($"players[{i}].actions[{j}].command", "Action has no command."));
                }

                if (action.Timing == null)
                {
                    diagnostics.Add(new ScenarioValidationDiagnostic($"players[{i}].actions[{j}].timing", "Action has no timing."));
                }
                else if (action.Timing is AfterActionTiming after
                    && !actionIds.Contains(after.TriggerActionId)
                    && !IsForwardReference(scenario, i, after.TriggerActionId))
                {
                    diagnostics.Add(new ScenarioValidationDiagnostic(
                        $"players[{i}].actions[{j}].timing.triggerActionId",
                        $"AfterAction timing references unknown action id '{after.TriggerActionId}'."));
                }

                if (action.Id != null && !actionIds.Add(action.Id))
                {
                    diagnostics.Add(new ScenarioValidationDiagnostic(
                        $"players[{i}].actions[{j}].id",
                        $"Action id '{action.Id}' is already used by an earlier action."));
                }
            }
        }
    }

    private static bool IsForwardReference(ReplayScenario scenario, int ownerIndex, string actionId)
    {
        for (var i = ownerIndex; i < scenario.Players.Count; i++)
        {
            foreach (var action in scenario.Players[i].Actions)
            {
                if (action?.Id == actionId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ValidatePlacements(ReplayScenario scenario, List<ScenarioValidationDiagnostic> diagnostics)
    {
        for (var i = 0; i < scenario.Placements.Count; i++)
        {
            var placement = scenario.Placements[i];
            if (placement == null)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"placements[{i}]", "Placement is null."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(placement.DefinitionName))
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"placements[{i}].definitionName", "Placement definition name is empty."));
            }

            if (placement.PlayerIndex < 0 || placement.PlayerIndex >= scenario.Players.Count)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic(
                    $"placements[{i}].playerIndex",
                    $"Player index {placement.PlayerIndex} is outside the player slots (0..{scenario.Players.Count - 1})."));
            }

            if (placement.Quantity <= 0)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"placements[{i}].quantity", "Quantity must be at least 1."));
            }

            if (placement.Position == null)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"placements[{i}].position", "Placement has no position."));
            }

            if (placement.Health is <= 0)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"placements[{i}].health", "Health must be positive when present."));
            }

            if (placement.MaxHealth is <= 0)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"placements[{i}].maxHealth", "Max health must be positive when present."));
            }

            if (placement.Health > placement.MaxHealth)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"placements[{i}].health", "Health exceeds max health."));
            }
        }
    }

    private static void ValidateParameters(ReplayScenario scenario, List<ScenarioValidationDiagnostic> diagnostics)
    {
        foreach (var parameter in scenario.Parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Key))
            {
                diagnostics.Add(new ScenarioValidationDiagnostic("parameters", "Parameter key is empty."));
            }
        }
    }

    private static void ValidateStopConditions(ReplayScenario scenario, List<ScenarioValidationDiagnostic> diagnostics)
    {
        for (var i = 0; i < scenario.StopConditions.Count; i++)
        {
            if (scenario.StopConditions[i] is FrameCapStopCondition { MaxFrames: <= 0 })
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"stopConditions[{i}].maxFrames", "Frame cap must be at least 1."));
            }
        }
    }

    private static void ValidateRecordedMetricSeries(ReplayScenario scenario, List<ScenarioValidationDiagnostic> diagnostics)
    {
        for (var i = 0; i < scenario.RecordedMetricSeries.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(scenario.RecordedMetricSeries[i]))
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"recordedMetricSeries[{i}]", "Metric series name is empty."));
            }
        }
    }

    private static void ValidateRecordingOptions(ReplayScenario scenario, List<ScenarioValidationDiagnostic> diagnostics)
    {
        if (scenario.Recording == null)
        {
            return;
        }

        if (scenario.Recording.SampleInterval == 0)
        {
            diagnostics.Add(new ScenarioValidationDiagnostic("recording.sampleInterval", "Sample interval must be at least 1."));
        }

        if (scenario.Recording.SnapshotInterval == 0)
        {
            diagnostics.Add(new ScenarioValidationDiagnostic("recording.snapshotInterval", "Snapshot interval must be at least 1."));
        }
    }

    private static void ValidateGenerators(ReplayScenario scenario, List<ScenarioValidationDiagnostic> diagnostics)
    {
        for (var i = 0; i < scenario.PopulationGenerators.Count; i++)
        {
            var generator = scenario.PopulationGenerators[i];
            var path = $"populationGenerators[{i}]";
            if (generator == null)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic(path, "Population generator is null."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(generator.DefinitionName))
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"{path}.definitionName", "Generator definition name is empty."));
            }

            if (generator.PlayerIndex < 0 || generator.PlayerIndex >= scenario.Players.Count)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic(
                    $"{path}.playerIndex",
                    $"Player index {generator.PlayerIndex} is outside the player slots (0..{scenario.Players.Count - 1})."));
            }

            if (generator.Health > generator.MaxHealth)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"{path}.health", "Health exceeds max health."));
            }

            if (generator is FormationPopulationGenerator formation && formation.Quantity == 0)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"{path}.quantity", "Formation quantity must be at least 1."));
            }

            if (generator is GridPopulationGenerator grid)
            {
                if (grid.Rows == 0 || grid.Columns == 0)
                {
                    diagnostics.Add(new ScenarioValidationDiagnostic($"{path}.rows", "Grid rows and columns must be at least 1."));
                }
            }

            if (generator is ScatteredPopulationGenerator scattered)
            {
                if (scattered.Quantity <= 0)
                {
                    diagnostics.Add(new ScenarioValidationDiagnostic($"{path}.quantity", "Scattered quantity must be at least 1."));
                }

                if (scattered.Radius <= 0)
                {
                    diagnostics.Add(new ScenarioValidationDiagnostic($"{path}.radius", "Scattered radius must be positive."));
                }
            }
        }

        for (var i = 0; i < scenario.CommandGenerators.Count; i++)
        {
            var generator = scenario.CommandGenerators[i];
            var path = $"commandGenerators[{i}]";
            if (generator == null)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic(path, "Command generator is null."));
                continue;
            }

            if (generator.PlayerIndex < 0 || generator.PlayerIndex >= scenario.Players.Count)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic(
                    $"{path}.playerIndex",
                    $"Player index {generator.PlayerIndex} is outside the player slots (0..{scenario.Players.Count - 1})."));
            }

            if (generator.Template.Count == 0)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"{path}.template", "Command generator template is empty."));
            }

            if (generator.RepeatCount == 0)
            {
                diagnostics.Add(new ScenarioValidationDiagnostic($"{path}.repeatCount", "Repeat count must be at least 1."));
            }
        }
    }

    private static void ValidateActionPositions(
        ReplayScenario scenario,
        MapMetadataService map,
        List<ScenarioValidationDiagnostic> diagnostics)
    {
        var rng = new Random(0);
        for (var i = 0; i < scenario.Players.Count; i++)
        {
            var enemyIdx = i == 0 ? 1 : 0;
            var ctx = TerrainValidator.BuildContextPublic(scenario, map, i, enemyIdx);
            for (var j = 0; j < scenario.Players[i].Actions.Count; j++)
            {
                var action = scenario.Players[i].Actions[j];
                if (action?.Position == null)
                {
                    continue;
                }

                Vector3 worldPos;
                try
                {
                    worldPos = action.Position.Resolve(ctx, rng);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new ScenarioValidationDiagnostic(
                        $"players[{i}].actions[{j}].position",
                        $"Position resolution failed: {ex.Message}"));
                    continue;
                }

                if (worldPos.X < map.ExtentMin.X || worldPos.X > map.ExtentMax.X
                    || worldPos.Y < map.ExtentMin.Y || worldPos.Y > map.ExtentMax.Y)
                {
                    diagnostics.Add(new ScenarioValidationDiagnostic(
                        $"players[{i}].actions[{j}].position",
                        $"Resolved position ({worldPos.X:F0}, {worldPos.Y:F0}) is outside map extents."));
                }
            }
        }
    }
}
