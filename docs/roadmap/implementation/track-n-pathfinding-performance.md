# Track N: Multi-unit pathfinding performance

Source of truth: [deep-dives/n-multi-unit-pathfinding.md](../deep-dives/n-multi-unit-pathfinding.md) :
[Architecture diagrams §8](../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams).

> Audience: human overview; agents load the cards.

| Card | Phase | Title | Checklist item | Status |
|---|---|---|---|---|
| [`WI-N0-01`](../agent/cards/WI-N0-01-path-span-and-counters.md) | N0 | `path` span and request counters by origin | N0-1 | Done |
| [`WI-N0-02`](../agent/cards/WI-N0-02-pathfinding-load-scenario.md) | N0 | `PathfindingLoad` scenario and baseline report | N0-2 | Done |
| [`WI-N1-01`](../agent/cards/WI-N1-01-allocation-free-search.md) | N1 | Allocation-free search scratch with a path-equality corpus | N1-1 | Ready |
| [`WI-N1-02`](../agent/cards/WI-N1-02-region-labels.md) | N1 | Connected-region labels and unreachable early reject | N1-2 | Blocked |
| [`WI-N1-03`](../agent/cards/WI-N1-03-passability-snapshot.md) | N1 | Versioned passability snapshot for workers | N1-3 | Blocked |
| [`WI-N2-01`](../agent/cards/WI-N2-01-follower-landing.md) | N2 | Bounded landing for blocked followers | N2-1 | Blocked |
| [`WI-N2-02`](../agent/cards/WI-N2-02-group-selection-moves.md) | N2 | Group moves for squad-less selections, behind an option | N2-2 | Blocked |
| [`WI-N3-01`](../agent/cards/WI-N3-01-goal-field-gate.md) | N3 | Shared-destination field gate | N3-1 | Blocked |
| [`WI-N4-01`](../agent/cards/WI-N4-01-pending-path-handles.md) | N4 | Pending path handles on `AIUpdate` | N4-1 | Blocked |
| [`WI-N4-02`](../agent/cards/WI-N4-02-async-order-paths.md) | N4 | Async order paths behind an option | N4-2 | Blocked |
| [`WI-N5-01`](../agent/cards/WI-N5-01-scale-verification.md) | N5 | Scale verification | N5-1 | Blocked |

Delivery: phase staging branch `staging/track-n`, numbered group PRs per the repository
development model. Suggested groups: 1 = N0, 2 = N1, 3 = N2 and N4, 4 = N3 and N5.

Each card records its measured numbers in the matching results section below.

## WI-N0-02 baseline

Recorded 2026-10-22, seed 1, headless, `mass-battle-deepdivide-capacity-probe.json` profile,
navigation grid 656x460. PathfindingLoad cases: one `OrderProcessor.MoveUnits` order per case,
30-tick measurement window, cleared between cases; per-search means divide the window span by
the window's synchronous requests.

| Case | Spawned | Squads at order | Order-tick span (ms) | Unit | SquadLeader | BlockedFollower | Nodes | Failed |
|---|---|---|---|---|---|---|---|---|
| squadless-1 | 1 | 0 | 3.47 | 1 | 0 | 0 | 7,756 | 0 |
| squadless-10 | 10 | 0 | 30.02 | 10 | 0 | 0 | 63,022 | 0 |
| squadless-100 | 100 | 0 | 247.66 | 100 | 0 | 0 | 459,909 | 0 |
| squadless-1000 | 1000 | 0 | 1376.37 | 1000 | 0 | 0 | 4,128,233 | 0 |
| armyai-squads-1 | 1 | 1 | 2.70 | 1 | 0 | 0 | 7,756 | 0 |
| armyai-squads-10 | 10 | 2 | 4.87 | 0 | 2 | 0 | 13,957 | 0 |
| armyai-squads-100 | 100 | 13 | 15.15 | 0 | 13 | 0 | 43,837 | 0 |
| armyai-squads-1000 | 1000 | 125 | 147.68 | 0 | 125 | 1 | 434,796 | 0 |
| squadless-10-impassable | 10 | 0 | 0.12 | 10 | 0 | 0 | 0 | 10 |

Mean wall time per synchronous search ranged from 1.17 ms (crowded spawn zones shorten paths)
to 3.47 ms (single unit, full map crossing). p99 equals the mean in every case because each
window carries its requests on a single tick.

Dominant origin: `Unit` for squad-less selections, `SquadLeader` under army AI. The unreachable
case found no enclosed impassable pocket within the placement bounds on the deepdivide map, so
it used the off-map fallback destination; every search aborted at the null end node (0 nodes
expanded, 0.012 ms mean), so the deep-dive's unreachable-goal region flood is not reproduced by
off-graph endpoints.

Verdicts against the deep-dive budgets:

| Budget | Value | Measured | Verdict |
|---|---|---|---|
| Span on a 1000-unit selection move tick | at or under 33.3 ms | 1376.37 ms squad-less, 147.68 ms army-AI squads | unmet; request sharing through squads cuts the order 9.3x but both modes exceed one frame, so N1 per-search cost and N2 shared group requests are both required |
| Full searches per multi-unit order | at or under groups plus region-crossing followers | 1000 unit searches squad-less, 126 leader plus blocked-follower searches across 125 squads under army AI | squad-less selections bypass the bound by construction (they are the N2 target); army-AI mode meets it (126 vs 125 groups plus 1 blocked follower) |

Measurement notes: `OrderProcessor.MoveUnits` skips squad bucketing for single-unit
selections, so `armyai-squads-1` issues a `Unit` request despite the unit belonging to a squad.
Failed searches and endpoint adjustments for the passable cases were zero; no async requests
were submitted (the scenario orders synchronously).

M4 army-AI probe (`MassBattleCapacityProbe`, army AI on, 4500 frames, seed 1): 992 vs 992 units
spawned, frame budget reached, all checks passed. Path span statistics over 4500 ticks:

| Sample set | Mean (ms) | Median (ms) | p99 (ms) | Max (ms) |
|---|---|---|---|---|
| All ticks (amortized) | 4.315 | 0.000 | 182.3 | 1366.5 |
| Nonzero ticks only (75 of 4500) | 258.9 | 232.8 | 437.7 | 1366.5 |

Nonzero ticks coincide with decision-execution ticks (75 = 4500/60): requests concentrate on
those ticks, so per-tick request counter medians are 0 by construction and span samples from
`systemSamples` are the meaningful signal. For comparison, `gameLogic` mean 30.6 ms,
`scene3d` mean 3.8 ms, `partition` mean 4.1 ms, whole-tick mean 44.0 ms.

| Budget | Value | Measured | Verdict |
|---|---|---|---|
| `system.path.spanMs` amortized, M4 army-AI | at or under 1.7 ms | median 0, mean 4.315 ms (13% of the 33.3 ms anchor) | unmet on mean share; zero-path ticks hide the cost from the median |
| p99 on order ticks, M4 army-AI | at or under 16.7 ms | 437.7 ms on nonzero ticks, 182.3 ms over all ticks | unmet, roughly 26x over on decision ticks |

## WI-N1 results

Pending.

## WI-N2 results

Pending.

## WI-N3-01 gate decision

Pending.

## WI-N4 results

Pending.

## WI-N5-01 verification

Pending.
