using System.Collections.Generic;
using System.Numerics;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Builds a deterministic <see cref="SpawnPlan"/> from a <see cref="ValidationGroundScenario"/>:
/// an interleaved formation of parallel rows (units, targets, units, targets, ...), one target per
/// attacker (so each unit's first-damage completion reflects its own weapon), and each unit's
/// primary action resolved through <see cref="PrimaryActionCatalog"/>. No
/// <c>IGame</c>/<c>Scene3D</c>/<c>GameLogic</c> dependency; positions are planar and the host
/// samples terrain height. See deep dive P9 §2.1, §2.4, §2.9.
/// </summary>
public static class ValidationGroundBuilder
{
    public static SpawnPlan Build(ValidationGroundScenario scenario)
    {
        var layout = scenario.Layout;
        var definitions = scenario.TargetDefinitions;
        var center = layout.TargetClusterCenter;
        var factionCount = scenario.FactionIndices.Count;
        var supplySources = BuildSupplySources(scenario, layout);

        // Index placement overrides for O(1) lookup by (faction, roster slot, isTarget). An override
        // with no matching generated placement is ignored (e.g. after the roster shrinks), so stale
        // entries never crash the build.
        var overrides = new Dictionary<(int Faction, int Roster, bool IsTarget), Vector2>();
        foreach (var o in scenario.PlacementOverrides)
        {
            overrides[(o.FactionIndex, o.RosterIndex, o.IsTarget)] = o.PlanarPosition;
        }

        var units = new List<UnitPlacement>();
        var targets = new List<TargetPlacement>();
        var globalUnitIndex = 0;

        // Six parallel rows: for each faction a unit row then its target row, interleaved
        // (units, targets, units, targets, ...). Rows run east-west (along X), centred on centre.X;
        // the stack is centred on centre.Y. Each unit's target is directly ahead (same X, one row
        // over), perpendicular to the row, so a unit's own target is always its closest target and
        // units never interleave with their own or a neighbour faction's targets.
        var rowGapUnitTarget = layout.TargetApproachDistance;
        var rowGapFaction = layout.FactionRowOffset;
        var totalSpan = factionCount * rowGapUnitTarget + (factionCount - 1) * rowGapFaction;
        var startRowY = center.Y - totalSpan / 2f;

        for (var row = 0; row < factionCount; row++)
        {
            var factionIndex = scenario.FactionIndices[row];
            var roster = UnitCatalog.ForFaction(factionIndex);
            var unitRowY = startRowY + row * (rowGapUnitTarget + rowGapFaction);
            var targetRowY = unitRowY + rowGapUnitTarget;
            var rowOriginX = center.X - (roster.Count - 1) * layout.UnitSpacing / 2f;

            for (var i = 0; i < roster.Count; i++)
            {
                var unit = roster[i];
                var unitPosX = rowOriginX + i * layout.UnitSpacing;
                // A per-object override replaces the knob-derived unit position; otherwise the row
                // math stands.
                var position = overrides.TryGetValue((factionIndex, i, false), out var unitOv)
                    ? new Vector3(unitOv.X, unitOv.Y, 0f)
                    : new Vector3(unitPosX, unitRowY, 0f);

                var action = PrimaryActionCatalog.Resolve(unit);

                // One target per unit, placed directly in front of it on the faction's target row
                // (same X). A shared target would complete every unit assigned to it on the first hit
                // (conflating timings); this short, per-unit placement keeps each unit's first-damage
                // duration close to its weapon delay. See deep dive P9 §2.4, §2.5. A target override
                // replaces it.
                var targetPos = overrides.TryGetValue((factionIndex, i, true), out var targetOv)
                    ? new Vector3(targetOv.X, targetOv.Y, 0f)
                    : new Vector3(position.X, targetRowY, 0f);
                var targetDef = definitions[globalUnitIndex % definitions.Count];
                targets.Add(new TargetPlacement(targetDef, targetPos));

                units.Add(new UnitPlacement(
                    unit.DefinitionName,
                    factionIndex,
                    PlayerIndex: factionIndex,
                    Position: position,
                    Action: action,
                    TargetIndex: globalUnitIndex,
                    RosterIndex: i));

                globalUnitIndex++;
            }
        }

        return new SpawnPlan
        {
            UnitPlacements = units,
            TargetPlacements = targets,
            SupplySourcePlacements = supplySources,
        };
    }

    private static List<TargetPlacement> BuildSupplySources(ValidationGroundScenario scenario, LayoutConfig layout)
    {
        var sources = new List<TargetPlacement>();
        var center = layout.TargetClusterCenter;
        for (var i = 0; i < scenario.SupplySourceDefinitions.Count; i++)
        {
            var position = new Vector3(
                center.X + layout.TargetClusterRadius * 2 + i * layout.UnitSpacing,
                center.Y,
                0f);
            sources.Add(new TargetPlacement(scenario.SupplySourceDefinitions[i], position));
        }

        return sources;
    }
}
