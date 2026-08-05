using System.Collections.Generic;
using System.Numerics;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Builds a deterministic <see cref="SpawnPlan"/> from a <see cref="ValidationGroundScenario"/>:
/// one row per faction facing the shared target cluster, one target per attacker (so each unit's
/// first-damage completion reflects its own weapon), and each unit's primary action resolved
/// through <see cref="PrimaryActionCatalog"/>. No <c>IGame</c>/<c>Scene3D</c>/<c>GameLogic</c>
/// dependency; positions are planar and the host samples terrain height. See deep dive P9 §2.1,
/// §2.4, §2.9.
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
        for (var row = 0; row < factionCount; row++)
        {
            var factionIndex = scenario.FactionIndices[row];
            var roster = UnitCatalog.ForFaction(factionIndex);
            var rowOrigin = FactionRowOrigin(layout, row, factionCount);

            for (var i = 0; i < roster.Count; i++)
            {
                var unit = roster[i];
                var derivedPosition = new Vector3(
                    rowOrigin.X + i * layout.UnitSpacing,
                    rowOrigin.Y,
                    0f);
                // A per-object override replaces the knob-derived unit position; otherwise the row
                // math stands.
                var position = overrides.TryGetValue((factionIndex, i, false), out var unitOv)
                    ? new Vector3(unitOv.X, unitOv.Y, 0f)
                    : derivedPosition;

                var action = PrimaryActionCatalog.Resolve(unit);

                // One target per unit, placed a short, fixed distance in front of the unit toward
                // the cluster centre. A shared target would complete every unit assigned to it on
                // the first hit (conflating timings), and a far target would make the measurement
                // travel-dominated; a short, per-unit approach keeps each unit's first-damage
                // duration close to its weapon delay. See deep dive P9 §2.4, §2.5. The target derives
                // from the (possibly overridden) unit position, and a target override replaces it.
                var toCenter = new Vector2(center.X - position.X, center.Y - position.Y);
                var dir = toCenter.Length() > 0f ? Vector2.Normalize(toCenter) : new Vector2(1f, 0f);
                var derivedTargetPos = new Vector3(
                    position.X + dir.X * layout.TargetApproachDistance,
                    position.Y + dir.Y * layout.TargetApproachDistance,
                    0f);
                var targetPos = overrides.TryGetValue((factionIndex, i, true), out var targetOv)
                    ? new Vector3(targetOv.X, targetOv.Y, 0f)
                    : derivedTargetPos;
                var targetDef = definitions[globalUnitIndex % definitions.Count];
                targets.Add(new TargetPlacement(targetDef, targetPos));

                units.Add(new UnitPlacement(
                    unit.DefinitionName,
                    factionIndex,
                    PlayerIndex: factionIndex,
                    Position: position,
                    Action: action,
                    TargetIndex: globalUnitIndex));

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

    // Each faction row sits on the opposite side of the target cluster, offset laterally so the
    // rows are parallel and facing the centre. rowCount centres the block vertically over the
    // cluster; it must match the actual faction count, not a hard-coded 3.
    private static Vector3 FactionRowOrigin(LayoutConfig layout, int rowIndex, int rowCount)
    {
        var totalHeight = (rowCount - 1) * layout.FactionRowOffset;
        var startY = layout.TargetClusterCenter.Y - totalHeight / 2f;
        return new Vector3(
            layout.TargetClusterCenter.X - layout.FactionRowDistance,
            startY + rowIndex * layout.FactionRowOffset,
            0f);
    }
}
