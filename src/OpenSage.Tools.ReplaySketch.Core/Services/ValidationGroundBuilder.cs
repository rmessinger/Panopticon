using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Builds a deterministic <see cref="SpawnPlan"/> from a <see cref="ValidationGroundScenario"/>:
/// one target per attacker (so each unit's first-damage completion reflects its own weapon), and
/// each unit's primary action resolved through <see cref="PrimaryActionCatalog"/>. Two arrangement
/// modes (see <see cref="LayoutMode"/>): interleaved parallel rows, or per-faction clusters packed
/// into terrain-flat, structure-free regions found through height sampling. No
/// <c>IGame</c>/<c>Scene3D</c>/<c>GameLogic</c> dependency; positions are planar and the host
/// samples terrain height. See deep dive P9 §2.1, §2.4, §2.9.
/// </summary>
public static class ValidationGroundBuilder
{
    // Cluster search resolution: candidate centres are probed on a grid of this step over the map.
    private const float ClusterSearchStep = 100f;
    // Margin kept between a cluster's outermost placement and the playable extent border.
    private const float ClusterBorderMargin = 30f;
    // Height spreads within this many units count as equally flat when scoring candidates, so the
    // tie breaks by proximity to the layout centre. Without it, a strictly-flatter first cluster
    // can sit next to the only other flat disc on the map and box later factions into the hills
    // (observed on Alpine Assault: spread 0 vs 2 chose the 0 and left none flat for faction 3).
    private const float FlatnessBucket = 5f;

    public static SpawnPlan Build(ValidationGroundScenario scenario, MapMetadataService? map = null)
    {
        var layout = scenario.Layout;
        var definitions = scenario.TargetDefinitions;
        var center = layout.TargetClusterCenter;
        var factionCount = scenario.FactionIndices.Count;
        var supplySources = BuildSupplySources(scenario, layout);

        // Index placement overrides for O(1) lookup by (faction, roster slot, isTarget). An override
        // with no matching generated placement is ignored (e.g. after the roster shrinks), so stale
        // entries never crash the build.
        var overrides = new Dictionary<(int Faction, int Roster, bool IsTarget), PlacementOverride>();
        foreach (var o in scenario.PlacementOverrides)
        {
            overrides[(o.FactionIndex, o.RosterIndex, o.IsTarget)] = o;
        }

        var units = new List<UnitPlacement>();
        var targets = new List<TargetPlacement>();
        var globalUnitIndex = 0;

        if (layout.Mode == LayoutMode.Clusters)
        {
            BuildClusters(scenario, map, overrides, definitions, units, targets, ref globalUnitIndex);
        }
        else
        {
            BuildRows(scenario, overrides, definitions, units, targets, ref globalUnitIndex);
        }

        return new SpawnPlan
        {
            UnitPlacements = units,
            TargetPlacements = targets,
            SupplySourcePlacements = supplySources,
        };
    }

    /// <summary>
    /// Rows mode: six parallel rows (units, targets, units, targets, ...) running east-west,
    /// centred on <see cref="LayoutConfig.TargetClusterCenter"/>. Each unit's target is directly
    /// ahead (same X), so a unit's own target is always its closest target.
    /// </summary>
    private static void BuildRows(
        ValidationGroundScenario scenario,
        Dictionary<(int Faction, int Roster, bool IsTarget), PlacementOverride> overrides,
        IReadOnlyList<string> definitions,
        List<UnitPlacement> units,
        List<TargetPlacement> targets,
        ref int globalUnitIndex)
    {
        var layout = scenario.Layout;
        var center = layout.TargetClusterCenter;
        var rowGapUnitTarget = layout.TargetApproachDistance;
        var rowGapFaction = layout.FactionRowOffset;
        var totalSpan = scenario.FactionIndices.Count * rowGapUnitTarget + (scenario.FactionIndices.Count - 1) * rowGapFaction;
        var startRowY = center.Y - totalSpan / 2f;

        for (var row = 0; row < scenario.FactionIndices.Count; row++)
        {
            var factionIndex = scenario.FactionIndices[row];
            var roster = UnitCatalog.ForFaction(factionIndex);
            var unitRowY = startRowY + row * (rowGapUnitTarget + rowGapFaction);
            var targetRowY = unitRowY + rowGapUnitTarget;
            var rowOriginX = center.X - (roster.Count - 1) * layout.UnitSpacing / 2f;

            for (var i = 0; i < roster.Count; i++)
            {
                var unit = roster[i];
                if (SkipForGame(unit, scenario))
                {
                    continue;
                }

                var derived = new Vector2(rowOriginX + i * layout.UnitSpacing, unitRowY);
                AddPlacement(unit, factionIndex, i, derived, targetRowY - unitRowY, targetRowY,
                    overrides, definitions, units, targets, ref globalUnitIndex,
                    rowsMode: true);
            }
        }
    }

    /// <summary>
    /// Clusters mode: one cluster per faction. Each cluster centre is chosen by a deterministic
    /// scan that minimises local height spread and avoids map structures and the other clusters,
    /// preferring ground near <see cref="LayoutConfig.TargetClusterCenter"/>. Units form a compact
    /// block around the centre; each unit's target sits the standoff distance away on the block's
    /// outside, so targets never sit inside a neighbouring cluster.
    /// </summary>
    private static void BuildClusters(
        ValidationGroundScenario scenario,
        MapMetadataService? map,
        Dictionary<(int Faction, int Roster, bool IsTarget), PlacementOverride> overrides,
        IReadOnlyList<string> definitions,
        List<UnitPlacement> units,
        List<TargetPlacement> targets,
        ref int globalUnitIndex)
    {
        var layout = scenario.Layout;
        var chosen = new List<Vector2>();

        for (var row = 0; row < scenario.FactionIndices.Count; row++)
        {
            var factionIndex = scenario.FactionIndices[row];
            var roster = UnitCatalog.ForFaction(factionIndex);
            var activeCount = 0;
            for (var i = 0; i < roster.Count; i++)
            {
                if (!SkipForGame(roster[i], scenario))
                {
                    activeCount++;
                }
            }

            var clusterCenter = FindClusterCenter(map, layout, activeCount, chosen);
            chosen.Add(clusterCenter);

            var cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(activeCount)));
            var rowsCount = Math.Max(1, (int)Math.Ceiling(activeCount / (float)cols));
            var originX = clusterCenter.X - (cols - 1) * layout.UnitSpacing / 2f;
            var originY = clusterCenter.Y - (rowsCount - 1) * layout.UnitSpacing / 2f;

            var placed = 0;
            for (var i = 0; i < roster.Count; i++)
            {
                var unit = roster[i];
                if (SkipForGame(unit, scenario))
                {
                    continue;
                }

                var col = placed % cols;
                var gridRow = placed / cols;
                placed++;

                var derived = new Vector2(originX + col * layout.UnitSpacing, originY + gridRow * layout.UnitSpacing);
                AddPlacement(unit, factionIndex, i, derived, layout.TargetApproachDistance, 0f,
                    overrides, definitions, units, targets, ref globalUnitIndex,
                    rowsMode: false, clusterCenter: clusterCenter);
            }
        }
    }

    /// <summary>
    /// Emits one unit and its linked target. Position overrides replace the derived unit position;
    /// a unit-side <see cref="PlacementOverride.TargetDistance"/> replaces the standoff; a target
    /// override replaces the target position outright. Row mode pins targets to the faction's
    /// target row (tracking the unit's X); cluster mode places them outward from the block.
    /// </summary>
    private static void AddPlacement(
        UnitOption unit,
        int factionIndex,
        int rosterIndex,
        Vector2 derivedUnitPosition,
        float standoff,
        float targetRowY,
        Dictionary<(int Faction, int Roster, bool IsTarget), PlacementOverride> overrides,
        IReadOnlyList<string> definitions,
        List<UnitPlacement> units,
        List<TargetPlacement> targets,
        ref int globalUnitIndex,
        bool rowsMode,
        Vector2? clusterCenter = null)
    {
        overrides.TryGetValue((factionIndex, rosterIndex, false), out var unitOverride);
        overrides.TryGetValue((factionIndex, rosterIndex, true), out var targetOverride);

        var unitPos = unitOverride is { HasPosition: true }
            ? unitOverride.PlanarPosition
            : derivedUnitPosition;
        var effectiveStandoff = unitOverride?.TargetDistance ?? standoff;

        Vector2 targetPos;
        if (targetOverride is { HasPosition: true })
        {
            targetPos = targetOverride.PlanarPosition;
        }
        else if (rowsMode)
        {
            // Row semantics: the target tracks the unit's X but stays on the faction's target row,
            // even when the unit's position was overridden.
            targetPos = new Vector2(unitPos.X, targetRowY);
        }
        else
        {
            // Cluster mode: the target sits on the outside of the block relative to its unit, so
            // targets ring the cluster instead of crossing through it.
            var outward = unitPos - clusterCenter!.Value;
            var direction = outward == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(outward);
            targetPos = unitPos + direction * effectiveStandoff;
        }

        var action = PrimaryActionCatalog.Resolve(unit);
        targets.Add(new TargetPlacement(definitions[globalUnitIndex % definitions.Count], new Vector3(targetPos.X, targetPos.Y, 0f)));
        units.Add(new UnitPlacement(
            unit.DefinitionName,
            factionIndex,
            PlayerIndex: factionIndex,
            Position: new Vector3(unitPos.X, unitPos.Y, 0f),
            Action: action,
            TargetIndex: globalUnitIndex,
            RosterIndex: rosterIndex));

        globalUnitIndex++;
    }

    /// <summary>
    /// Deterministic cluster-centre search: probe candidates on a fixed grid over the playable
    /// extent, score each by height spread over the cluster disc (structures inside the disc
    /// disqualify), prefer proximity to the layout centre, and keep clear of already-chosen
    /// clusters. Without a map (unit tests) the terrain term is uniform, so the search degenerates
    /// to the nearest valid grid point to the layout centre, spaced along X.
    /// </summary>
    private static Vector2 FindClusterCenter(MapMetadataService? map, LayoutConfig layout, int rosterCount, List<Vector2> chosen)
    {
        var spacing = layout.UnitSpacing;
        var standoff = layout.TargetApproachDistance;
        var cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, rosterCount))));
        var rowsCount = Math.Max(1, (int)Math.Ceiling(Math.Max(1, rosterCount) / (float)cols));
        var clusterRadius = Math.Max((cols - 1), (rowsCount - 1)) * spacing / 2f + standoff + ClusterBorderMargin;

        // Two cluster centres must be far enough apart that their discs (units block + standoff
        // target ring + margin) cannot touch or interleave. Exact disc non-overlap is sufficient:
        // the standoff targets are inside their own cluster's disc. A larger gap (adding
        // FactionRowOffset on top) can exhaust the map's flat ground — on Alpine Assault only two
        // mutually 610-apart flat discs exist, forcing the third cluster into the hills.
        var separation = Math.Max(layout.FactionRowOffset, 2 * clusterRadius);

        float minX, minY, maxX, maxY;
        if (map != null)
        {
            minX = map.ExtentMin.X + clusterRadius;
            minY = map.ExtentMin.Y + clusterRadius;
            maxX = map.ExtentMax.X - clusterRadius;
            maxY = map.ExtentMax.Y - clusterRadius;
        }
        else
        {
            // No terrain data: a box around the layout centre large enough for all clusters.
            var box = separation * 4f;
            minX = layout.TargetClusterCenter.X - box;
            minY = layout.TargetClusterCenter.Y - box;
            maxX = layout.TargetClusterCenter.X + box;
            maxY = layout.TargetClusterCenter.Y + box;
        }

        var preferred = new Vector2(layout.TargetClusterCenter.X, layout.TargetClusterCenter.Y);
        (int Bucket, float Dist, Vector2 Center)? best = null;

        for (var y = minY; y <= maxY; y += ClusterSearchStep)
        {
            for (var x = minX; x <= maxX; x += ClusterSearchStep)
            {
                var candidate = new Vector2(x, y);

                var tooClose = false;
                foreach (var c in chosen)
                {
                    if (Vector2.Distance(c, candidate) < separation)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose)
                {
                    continue;
                }

                if (map != null && StructureInsideDisc(map, candidate, clusterRadius))
                {
                    continue;
                }

                var spread = map != null ? HeightSpread(map, candidate, clusterRadius) : 0f;
                var dist = Vector2.Distance(candidate, preferred);

                // Flatness dominates, bucketed so near-equal spreads tie-break by proximity.
                var bucket = (int)(spread / FlatnessBucket);
                if (best == null
                    || bucket < best.Value.Bucket
                    || (bucket == best.Value.Bucket && dist < best.Value.Dist))
                {
                    best = (bucket, dist, candidate);
                }
            }
        }

        return best?.Center ?? preferred;
    }

    private static bool StructureInsideDisc(MapMetadataService map, Vector2 center, float radius)
    {
        foreach (var structure in map.CivilianStructures)
        {
            var p = structure.Position;
            if (Vector2.Distance(new Vector2(p.X, p.Y), center) < radius)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Max-minus-min of height samples over a coarse disc grid of the given radius.</summary>
    private static float HeightSpread(MapMetadataService map, Vector2 center, float radius)
    {
        var step = Math.Max(40f, radius / 3f);
        var min = float.MaxValue;
        var max = float.MinValue;
        for (var dy = -radius; dy <= radius; dy += step)
        {
            for (var dx = -radius; dx <= radius; dx += step)
            {
                if (dx * dx + dy * dy > radius * radius)
                {
                    continue;
                }
                var h = map.SampleHeight(center.X + dx, center.Y + dy);
                min = Math.Min(min, h);
                max = Math.Max(max, h);
            }
        }
        return min == float.MaxValue ? float.MaxValue : max - min;
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

    private static bool SkipForGame(UnitOption unit, ValidationGroundScenario scenario)
    {
        // Game-aware roster: skip entries the target game's data does not contain (Zero Hour-only
        // units on a vanilla Generals run) rather than spawning a resolution gap. The slot keeps
        // its place in the roster indexing so placement-override indices stay stable.
        return unit.ZeroHourOnly && !GameHasZeroHourRoster(scenario);
    }

    private static bool GameHasZeroHourRoster(ValidationGroundScenario scenario)
    {
        return Enum.TryParse<SageGame>(scenario.Game, ignoreCase: true, out var game)
            && game == SageGame.CncGeneralsZeroHour;
    }
}
