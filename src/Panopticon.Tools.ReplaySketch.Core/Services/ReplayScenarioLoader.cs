using System;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Loads the declarative scenario artifact from disk (WI-A5-03). Structural validation happens
/// here, before any host or game construction: unsupported schema versions and missing fields
/// fail with an error naming the artifact. Semantic validation (map existence, faction and
/// definition resolution) is WI-A5-10.
/// </summary>
public static class ReplayScenarioLoader
{
    public static ReplayScenario Load(string filePath)
    {
        var scenario = ScenarioProfileLoader.LoadFromFile(filePath);
        Validate(scenario, filePath);
        return scenario;
    }

    public static void Validate(ReplayScenario scenario, string artifactName)
    {
        if (scenario.SchemaVersion != ReplayScenario.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Scenario artifact '{artifactName}' uses schema version {scenario.SchemaVersion}; "
                + $"this build understands {ReplayScenario.CurrentSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(scenario.MapPath))
        {
            throw new InvalidOperationException(
                $"Scenario artifact '{artifactName}' has no map path.");
        }

        if (string.IsNullOrWhiteSpace(scenario.DomainId))
        {
            throw new InvalidOperationException(
                $"Scenario artifact '{artifactName}' has an empty domain id.");
        }

        if (scenario.Players.Count == 0)
        {
            throw new InvalidOperationException(
                $"Scenario artifact '{artifactName}' declares no players.");
        }

        for (var i = 0; i < scenario.Placements.Count; i++)
        {
            var placement = scenario.Placements[i];
            if (string.IsNullOrWhiteSpace(placement.DefinitionName))
            {
                throw new InvalidOperationException(
                    $"Scenario artifact '{artifactName}' placement {i} has no definition name.");
            }

            if (placement.PlayerIndex < 0 || placement.PlayerIndex >= scenario.Players.Count)
            {
                throw new InvalidOperationException(
                    $"Scenario artifact '{artifactName}' placement {i} names player slot "
                    + $"{placement.PlayerIndex}, but the scenario has {scenario.Players.Count} players.");
            }

            if (placement.Position is null)
            {
                throw new InvalidOperationException(
                    $"Scenario artifact '{artifactName}' placement {i} has no position.");
            }

            if (placement.Quantity <= 0)
            {
                throw new InvalidOperationException(
                    $"Scenario artifact '{artifactName}' placement {i} has quantity "
                    + $"{placement.Quantity}; it must be positive.");
            }
        }

        if (scenario.Recording is { } recording)
        {
            if (recording.SampleInterval == 0)
            {
                throw new InvalidOperationException(
                    $"Scenario artifact '{artifactName}' requests a zero recording sample interval.");
            }

            if (recording.SnapshotInterval == 0)
            {
                throw new InvalidOperationException(
                    $"Scenario artifact '{artifactName}' requests a zero recording snapshot interval.");
            }
        }
    }
}
