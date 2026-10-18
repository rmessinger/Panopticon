#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Recording;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Caller-supplied inputs a capture cannot derive from a run bundle (WI-A5-11): the map the new
/// scenario runs on, the player slots it uses, and the world-space extents positions normalize
/// against. Domain, parameters, and stop conditions carry over when supplied.
/// </summary>
public sealed record ScenarioCaptureOptions(
    string MapPath,
    IReadOnlyList<PlayerSlotConfig> Players,
    Vector3 ExtentMin,
    Vector3 ExtentMax,
    string? DomainId = null,
    Dictionary<string, string>? Parameters = null,
    List<StopCondition>? StopConditions = null);

/// <summary>
/// What the projection dropped or clamped (WI-A5-11). Captures are lossy by contract: transients
/// that the recorded summary does not carry are listed, and owners outside the player-slot range
/// are counted rather than guessed.
/// </summary>
public sealed record ScenarioCaptureDiagnostics(
    string SourceRunId,
    uint RequestedFrame,
    uint ProjectedFrame,
    int ProjectedPlacements,
    int UnmappedOwners,
    int DroppedCommands,
    IReadOnlyList<uint> MoneyByPlayer,
    IReadOnlyList<string> OmittedTransientFields);

/// <summary>The captured scenario plus its projection diagnostics (WI-A5-11).</summary>
public sealed record ScenarioCaptureResult(ReplayScenario Scenario, ScenarioCaptureDiagnostics Diagnostics);

/// <summary>
/// Derives a new scenario from one recorded frame of a run bundle (WI-A5-11). This is a
/// deliberately lossy projection, not a continuation-equivalent save: object placements, health,
/// and player money project from the recorded summary; everything else starts fresh. The source
/// run's command timeline is dropped (a captured scenario replays placements, not orders), and
/// the seed is not carried so the new run samples independently.
/// </summary>
public static class ScenarioCaptureService
{
    private static readonly string[] TransientsOmitted =
    [
        "orientation (facing)",
        "upgrades",
        "veterancy",
        "production queues",
        "team membership",
        "per-module state",
    ];

    public static ScenarioCaptureResult Capture(
        RunBundleContents bundle,
        WorldStateArtifactReader stateReader,
        uint frame,
        ScenarioCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(stateReader);
        ArgumentNullException.ThrowIfNull(options);

        var state = stateReader.ReconstructStateAt(frame);
        // The reader stamps the requested frame on the returned summary; a capture beyond the
        // recorded prefix projects the prefix-end state, so the diagnostic reports the clamp.
        var projectedFrame = Math.Min(frame, stateReader.LastFrame);
        var playerCount = options.Players.Count;

        var scenario = new ReplayScenario
        {
            MapPath = options.MapPath,
            DomainId = options.DomainId ?? "rts",
            Placements = ProjectPlacements(state, options, out var unmappedOwners),
        };

        var droppedCommands = 0;
        foreach (var slot in options.Players)
        {
            droppedCommands += slot.Actions.Count;
        }

        scenario.Players = options.Players.Select((slot, index) => new PlayerSlotConfig
        {
            Name = slot.Name,
            FactionIndex = slot.FactionIndex,
            Color = slot.Color,
            StartPosition = slot.StartPosition,
            Team = slot.Team,
            Actions = [],
            StartingMoney = index < state.PlayerMoney.Count ? state.PlayerMoney[index] : null,
        }).ToList();

        if (options.Parameters is not null)
        {
            foreach (var (key, value) in options.Parameters)
            {
                scenario.Parameters[key] = value;
            }
        }

        if (options.StopConditions is not null)
        {
            scenario.StopConditions.AddRange(options.StopConditions);
        }

        var diagnostics = new ScenarioCaptureDiagnostics(
            bundle.Manifest.RunId,
            frame,
            projectedFrame,
            scenario.Placements.Count,
            unmappedOwners,
            droppedCommands,
            state.PlayerMoney,
            TransientsOmitted);

        return new ScenarioCaptureResult(scenario, diagnostics);
    }

    private static List<ScenarioPlacement> ProjectPlacements(
        WorldStateSummary state,
        ScenarioCaptureOptions options,
        out int unmappedOwners)
    {
        unmappedOwners = 0;
        var placements = new List<ScenarioPlacement>(state.Objects.Count);
        var spanX = options.ExtentMax.X - options.ExtentMin.X;
        var spanY = options.ExtentMax.Y - options.ExtentMin.Y;

        foreach (var obj in state.Objects)
        {
            if (obj.OwnerIndex < 0 || obj.OwnerIndex >= options.Players.Count)
            {
                unmappedOwners++;
                continue;
            }

            // Horizontal plane only: the placement's height is sampled from the map at spawn
            // time (NormalizedPosition contract), engine height is not carried.
            var normX = spanX > 0 ? (obj.Position.X - options.ExtentMin.X) / spanX : 0f;
            var normY = spanY > 0 ? (obj.Position.Y - options.ExtentMin.Y) / spanY : 0f;

            placements.Add(new ScenarioPlacement
            {
                DefinitionName = obj.DefinitionName,
                PlayerIndex = obj.OwnerIndex,
                Position = new NormalizedPosition(normX, normY),
                Quantity = 1,
                Health = obj.Health,
                MaxHealth = obj.MaxHealth,
            });
        }

        return placements;
    }
}
