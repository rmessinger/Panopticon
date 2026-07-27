using System.Collections.Generic;
using System.Numerics;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Builds a deterministic <see cref="SpawnPlan"/> from a <see cref="ValidationGroundScenario"/>:
/// one row per faction facing the shared target cluster, target assignment by index
/// (<c>unitIndex % targetCount</c>), and each unit's primary action resolved through
/// <see cref="PrimaryActionCatalog"/>. No <c>IGame</c>/<c>Scene3D</c>/<c>GameLogic</c> dependency;
/// positions are planar and the host samples terrain height. See deep dive P9 §2.1, §2.4, §2.9.
/// </summary>
public static class ValidationGroundBuilder
{
    public static SpawnPlan Build(ValidationGroundScenario scenario)
    {
        var layout = scenario.Layout;
        var targets = BuildTargetCluster(scenario, layout);
        var supplySources = BuildSupplySources(scenario, layout);
        var targetCount = targets.Count > 0 ? targets.Count : 1;

        var units = new List<UnitPlacement>();
        var globalUnitIndex = 0;

        for (var row = 0; row < scenario.FactionIndices.Count; row++)
        {
            var factionIndex = scenario.FactionIndices[row];
            var roster = UnitCatalog.ForFaction(factionIndex);
            var rowOrigin = FactionRowOrigin(layout, row);

            for (var i = 0; i < roster.Count; i++)
            {
                var unit = roster[i];
                var position = new Vector3(
                    rowOrigin.X + i * layout.UnitSpacing,
                    rowOrigin.Y,
                    0f);

                var action = PrimaryActionCatalog.Resolve(unit);
                var targetIndex = globalUnitIndex % targetCount;

                units.Add(new UnitPlacement(
                    unit.DefinitionName,
                    factionIndex,
                    PlayerIndex: factionIndex,
                    Position: position,
                    Action: action,
                    TargetIndex: targetIndex));

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

    private static List<TargetPlacement> BuildTargetCluster(ValidationGroundScenario scenario, LayoutConfig layout)
    {
        var targets = new List<TargetPlacement>();
        var count = scenario.TargetDefinitions.Count;
        for (var i = 0; i < count; i++)
        {
            var angle = (System.MathF.PI * 2 * i) / System.Math.Max(1, count);
            var position = new Vector3(
                layout.TargetClusterCenter.X + layout.TargetClusterRadius * System.MathF.Cos(angle),
                layout.TargetClusterCenter.Y + layout.TargetClusterRadius * System.MathF.Sin(angle),
                0f);
            targets.Add(new TargetPlacement(scenario.TargetDefinitions[i], position));
        }

        return targets;
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
    // rows are parallel and facing the centre.
    private static Vector3 FactionRowOrigin(LayoutConfig layout, int rowIndex)
    {
        var rowCount = 3;
        var totalHeight = (rowCount - 1) * layout.FactionRowOffset;
        var startY = layout.TargetClusterCenter.Y - totalHeight / 2f;
        return new Vector3(
            layout.TargetClusterCenter.X - layout.FactionRowDistance,
            startY + rowIndex * layout.FactionRowOffset,
            0f);
    }
}
