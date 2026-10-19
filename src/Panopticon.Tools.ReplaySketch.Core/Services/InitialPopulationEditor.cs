#nullable enable

using System.Collections.Generic;
using System.Linq;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Card WI-A5-14: UI-independent editor for a scenario's initial population. Select, add, move,
/// delete, and owner-change all mutate the wrapped <see cref="ReplayScenario"/> through this
/// controller only; every mutation is validated through <see cref="ScenarioValidator"/> first and
/// a failed edit leaves the scenario untouched and reports diagnostics. Domain-agnostic: the
/// placement list is spatial, but validation is structural, so graph-domain scenarios edit
/// identically (non-spatial topology authoring is WI-A5-33).
/// </summary>
public sealed class InitialPopulationEditor
{
    private readonly ReplayScenario _scenario;

    public InitialPopulationEditor(ReplayScenario scenario)
    {
        _scenario = scenario;
    }

    public IReadOnlyList<ScenarioPlacement> Placements => _scenario.Placements;

    public int SelectedIndex { get; private set; } = -1;

    public ScenarioPlacement? Selected => SelectedIndex >= 0 && SelectedIndex < Placements.Count
        ? Placements[SelectedIndex]
        : null;

    public IReadOnlyList<string> LastDiagnostics { get; private set; } = [];

    /// <summary>Selects the placement at <paramref name="index"/>; a negative index clears the selection.</summary>
    public void Select(int index)
    {
        SelectedIndex = index >= 0 && index < Placements.Count ? index : -1;
    }

    /// <summary>Appends a placement after validation; on failure the scenario is unchanged.</summary>
    public bool Add(string definitionName, int playerIndex, PositionSpec position, int quantity = 1, float? health = null, float? maxHealth = null)
    {
        var placement = new ScenarioPlacement
        {
            DefinitionName = definitionName,
            PlayerIndex = playerIndex,
            Position = position,
            Quantity = quantity,
            Health = health,
            MaxHealth = maxHealth,
        };
        var candidate = _scenario.Placements.ToList();
        candidate.Add(placement);
        return TryMutate(candidate);
    }

    /// <summary>Moves the selected placement to a new position after validation.</summary>
    public bool MoveSelected(PositionSpec newPosition)
    {
        if (Selected is not { } current)
        {
            LastDiagnostics = ["no placement selected"];
            return false;
        }

        var moved = CloneWith(current, position: newPosition);
        return TryMutate(Replaced(moved));
    }

    /// <summary>Deletes the selected placement.</summary>
    public bool DeleteSelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Placements.Count)
        {
            LastDiagnostics = ["no placement selected"];
            return false;
        }

        var candidate = Placements.ToList();
        candidate.RemoveAt(SelectedIndex);
        var removed = TryMutate(candidate);
        if (removed)
        {
            SelectedIndex = -1;
        }

        return removed;
    }

    /// <summary>Changes the selected placement's owning player after validation.</summary>
    public bool ChangeOwnerSelected(int newPlayerIndex)
    {
        if (Selected is not { } current)
        {
            LastDiagnostics = ["no placement selected"];
            return false;
        }

        var reowned = CloneWith(current, playerIndex: newPlayerIndex);
        return TryMutate(Replaced(reowned));
    }

    private List<ScenarioPlacement> Replaced(ScenarioPlacement replacement)
    {
        var candidate = Placements.ToList();
        candidate[SelectedIndex] = replacement;
        return candidate;
    }

    private bool TryMutate(List<ScenarioPlacement> candidatePlacements)
    {
        // WI-A5-14: validation runs on a candidate clone; the wrapped scenario only changes when
        // every placement (new and pre-existing) passes.
        var candidate = CloneScenario(candidatePlacements);
        var report = ScenarioValidator.Validate(candidate, null);
        var diagnostics = report.Diagnostics
            .Select(d => $"{d.FieldPath}: {d.Message}")
            .ToList();
        LastDiagnostics = diagnostics;
        if (!report.IsValid)
        {
            return false;
        }

        _scenario.Placements = candidatePlacements;
        if (SelectedIndex >= candidatePlacements.Count)
        {
            SelectedIndex = candidatePlacements.Count - 1;
        }

        return true;
    }

    private ReplayScenario CloneScenario(List<ScenarioPlacement> placements)
    {
        var clone = new ReplayScenario
        {
            SchemaVersion = _scenario.SchemaVersion,
            DomainId = _scenario.DomainId,
            Seed = _scenario.Seed,
            MapPath = _scenario.MapPath,
            BaseRadiusWorldUnits = _scenario.BaseRadiusWorldUnits,
            Placements = placements,
            Parameters = new Dictionary<string, string>(_scenario.Parameters),
            StopConditions = [.. _scenario.StopConditions],
            RecordedMetricSeries = [.. _scenario.RecordedMetricSeries],
            Players = [.. _scenario.Players],
        };
        return clone;
    }

    private static ScenarioPlacement CloneWith(
        ScenarioPlacement placement,
        PositionSpec? position = null,
        int? playerIndex = null)
    {
        return new ScenarioPlacement
        {
            DefinitionName = placement.DefinitionName,
            PlayerIndex = playerIndex ?? placement.PlayerIndex,
            Position = position ?? placement.Position,
            Quantity = placement.Quantity,
            Health = placement.Health,
            MaxHealth = placement.MaxHealth,
        };
    }
}
