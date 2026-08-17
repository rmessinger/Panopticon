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
        var clusterInfos = new List<(Vector2 Center, float Radius)>();

        if (layout.Mode == LayoutMode.Clusters)
        {
            BuildClusters(scenario, map, overrides, definitions, units, targets, ref globalUnitIndex, clusterInfos);
        }
        else
        {
            BuildRows(scenario, overrides, definitions, units, targets, ref globalUnitIndex);
        }

        // Supply sources are placed after the formation: in cluster mode they must clear the
        // cluster discs, which are only known once clusters are chosen.
        var supplySources = BuildSupplySources(scenario, layout, clusterInfos);

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
    /// block around the centre; each unit's target sits the standoff distance away in the outward
    /// direction rotated to maximise clearance from the block's units and already-placed targets
    /// (a purely radial placement lands interior units' targets exactly on their neighbours).
    /// </summary>
    private static void BuildClusters(
        ValidationGroundScenario scenario,
        MapMetadataService? map,
        Dictionary<(int Faction, int Roster, bool IsTarget), PlacementOverride> overrides,
        IReadOnlyList<string> definitions,
        List<UnitPlacement> units,
        List<TargetPlacement> targets,
        ref int globalUnitIndex,
        List<(Vector2 Center, float Radius)> clusterInfos)
    {
        var layout = scenario.Layout;
        var chosen = new List<Vector2>();
        var placedTargetPositions = new List<Vector2>();

        for (var row = 0; row < scenario.FactionIndices.Count; row++)
        {
            var factionIndex = scenario.FactionIndices[row];
            var roster = UnitCatalog.ForFaction(factionIndex);
            var activeIndices = new List<int>();
            for (var i = 0; i < roster.Count; i++)
            {
                if (!SkipForGame(roster[i], scenario))
                {
                    activeIndices.Add(i);
                }
            }
            var activeCount = activeIndices.Count;

            var clusterCenter = FindClusterCenter(map, layout, activeCount, chosen);
            chosen.Add(clusterCenter);
            clusterInfos.Add((clusterCenter, ClusterRadius(layout, activeCount)));

            var cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, activeCount))));
            var rowsCount = Math.Max(1, (int)Math.Ceiling(Math.Max(1, activeCount) / (float)cols));
            var originX = clusterCenter.X - (cols - 1) * layout.UnitSpacing / 2f;
            var originY = clusterCenter.Y - (rowsCount - 1) * layout.UnitSpacing / 2f;

            // All block positions are computed upfront: target placement needs the full block
            // footprint for its clearance checks, not just the units emitted so far.
            var blockPositions = new List<Vector2>(Math.Max(1, activeCount));
            for (var slot = 0; slot < activeCount; slot++)
            {
                var col = slot % cols;
                var gridRow = slot / cols;
                blockPositions.Add(new Vector2(originX + col * layout.UnitSpacing, originY + gridRow * layout.UnitSpacing));
            }

            for (var slot = 0; slot < activeCount; slot++)
            {
                var unit = roster[activeIndices[slot]];
                AddPlacement(unit, factionIndex, activeIndices[slot], blockPositions[slot], layout.TargetApproachDistance, 0f,
                    overrides, definitions, units, targets, ref globalUnitIndex,
                    rowsMode: false, clusterCenter: clusterCenter,
                    blockUnitPositions: blockPositions, placedTargetPositions: placedTargetPositions);
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
        Vector2? clusterCenter = null,
        IReadOnlyList<Vector2>? blockUnitPositions = null,
        List<Vector2>? placedTargetPositions = null)
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
            // Cluster mode: the target sits the standoff away in the outward direction rotated to
            // maximise clearance from the block's units and already-placed targets; a purely
            // radial placement lands interior units' targets exactly on their neighbours.
            targetPos = ChooseClearedTargetPosition(
                unitPos, clusterCenter!.Value, effectiveStandoff, blockUnitPositions!, placedTargetPositions!);
        }
        placedTargetPositions?.Add(targetPos);

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
    /// Candidate target positions at the exact standoff from the unit in the outward direction and
    /// its rotations; the one with the greatest minimum distance to the block's units and the
    /// already-placed targets wins (first candidate breaks ties, so pure-outward is preferred).
    /// Distance is always exactly the standoff, preserving weapon-range and timing semantics.
    /// </summary>
    private static Vector2 ChooseClearedTargetPosition(
        Vector2 unitPos,
        Vector2 clusterCenter,
        float standoff,
        IReadOnlyList<Vector2> blockUnitPositions,
        List<Vector2> placedTargetPositions)
    {
        var outward = unitPos - clusterCenter;
        var baseDirection = outward == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(outward);

        // Rotation candidates in preference order: straight out, then progressively further around.
        var rotations = new[] { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };

        var bestPos = unitPos + baseDirection * standoff;
        var bestClearance = -1f;
        foreach (var rotation in rotations)
        {
            var direction = Vector2.Transform(baseDirection, System.Numerics.Matrix3x2.CreateRotation(MathF.PI * rotation / 180f));
            var candidate = unitPos + direction * standoff;

            var clearance = MinDistance(candidate, blockUnitPositions);
            if (placedTargetPositions.Count > 0)
            {
                clearance = MathF.Min(clearance, MinDistance(candidate, placedTargetPositions));
            }

            if (clearance > bestClearance)
            {
                bestClearance = clearance;
                bestPos = candidate;
            }
        }

        return bestPos;
    }

    private static float MinDistance(Vector2 point, IReadOnlyList<Vector2> others)
    {
        var min = float.MaxValue;
        foreach (var other in others)
        {
            min = MathF.Min(min, Vector2.Distance(point, other));
        }
        return min;
    }

    /// <summary>
    /// Radius of a faction cluster's disc: the units-block half-extent plus the standoff target
    /// ring plus a border margin. Cluster separation and structure avoidance both use this.
    /// </summary>
    private static float ClusterRadius(LayoutConfig layout, int rosterCount)
    {
        var cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, rosterCount))));
        var rowsCount = Math.Max(1, (int)Math.Ceiling(Math.Max(1, rosterCount) / (float)cols));
        return Math.Max((cols - 1), (rowsCount - 1)) * layout.UnitSpacing / 2f
            + layout.TargetApproachDistance + ClusterBorderMargin;
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
        var clusterRadius = ClusterRadius(layout, rosterCount);

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

    private static List<TargetPlacement> BuildSupplySources(
        ValidationGroundScenario scenario,
        LayoutConfig layout,
        List<(Vector2 Center, float Radius)> clusterInfos)
    {
        var sources = new List<TargetPlacement>();
        var center = layout.TargetClusterCenter;

        // Rows mode (or no clusters chosen): the historical offset east of the layout centre.
        if (layout.Mode != LayoutMode.Clusters || clusterInfos.Count == 0)
        {
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

        // Cluster mode: the layout-centre offset can land inside a terrain-derived cluster disc,
        // spawning a supply building on top of the roster. Deterministic search for the nearest
        // grid point to the layout centre that clears every disc (plus a margin) and every
        // already-placed source.
        const float sourceStep = 50f;
        const float sourceMargin = 60f;
        var placed = new List<Vector2>();
        for (var i = 0; i < scenario.SupplySourceDefinitions.Count; i++)
        {
            (float Dist, Vector2 Pos)? best = null;
            for (var y = center.Y - 1000f; y <= center.Y + 1000f; y += sourceStep)
            {
                for (var x = center.X - 1000f; x <= center.X + 1000f; x += sourceStep)
                {
                    var candidate = new Vector2(x, y);
                    var clear = true;
                    foreach (var (clusterCenter, radius) in clusterInfos)
                    {
                        if (Vector2.Distance(candidate, clusterCenter) < radius + sourceMargin)
                        {
                            clear = false;
                            break;
                        }
                    }
                    if (!clear)
                    {
                        continue;
                    }
                    if (placed.Count > 0 && MinDistance(candidate, placed) < layout.UnitSpacing)
                    {
                        continue;
                    }

                    var dist = Vector2.Distance(candidate, new Vector2(center.X, center.Y));
                    if (best == null || dist < best.Value.Dist)
                    {
                        best = (dist, candidate);
                    }
                }
            }

            var position = best?.Pos
                ?? new Vector2(center.X + layout.TargetClusterRadius * 2 + i * layout.UnitSpacing, center.Y);
            placed.Add(position);
            sources.Add(new TargetPlacement(scenario.SupplySourceDefinitions[i], new Vector3(position.X, position.Y, 0f)));
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
