# Track N: Multi-unit pathfinding performance

Source of truth: [deep-dives/n-multi-unit-pathfinding.md](../deep-dives/n-multi-unit-pathfinding.md) :
[Architecture diagrams §8](../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams).

> Audience: human overview; agents load the cards.

| Card | Phase | Title | Checklist item | Status |
|---|---|---|---|---|
| [`WI-N0-01`](../agent/cards/WI-N0-01-path-span-and-counters.md) | N0 | `path` span and request counters by origin | N0-1 | Done |
| [`WI-N0-02`](../agent/cards/WI-N0-02-pathfinding-load-scenario.md) | N0 | `PathfindingLoad` scenario and baseline report | N0-2 | Done |
| [`WI-N1-01`](../agent/cards/WI-N1-01-allocation-free-search.md) | N1 | Allocation-free search scratch with a path-equality corpus | N1-1 | Done |
| [`WI-N1-02`](../agent/cards/WI-N1-02-region-labels.md) | N1 | Connected-region labels and unreachable early reject | N1-2 | Done |
| [`WI-N1-03`](../agent/cards/WI-N1-03-passability-snapshot.md) | N1 | Versioned passability snapshot for workers | N1-3 | Done |
| [`WI-N2-01`](../agent/cards/WI-N2-01-follower-landing.md) | N2 | Bounded landing for blocked followers | N2-1 | Done |
| [`WI-N2-02`](../agent/cards/WI-N2-02-group-selection-moves.md) | N2 | Group moves for squad-less selections, behind an option | N2-2 | Done |
| [`WI-N3-01`](../agent/cards/WI-N3-01-goal-field-gate.md) | N3 | Shared-destination field gate | N3-1 | Done |
| [`WI-N4-01`](../agent/cards/WI-N4-01-pending-path-handles.md) | N4 | Pending path handles on `AIUpdate` | N4-1 | Done |
| [`WI-N4-02`](../agent/cards/WI-N4-02-async-order-paths.md) | N4 | Async order paths behind an option | N4-2 | Done |
| [`WI-N5-01`](../agent/cards/WI-N5-01-scale-verification.md) | N5 | Scale verification | N5-1 | Ready |

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

WI-N1-01, allocation-free search scratch, measured by the `PathCorpusSearch` benchmark: one
invocation = one pass over the 31-case path-equality corpus (deepdivide grid 656x460).
Per-search values divide by 31.

| Version | Corpus pass | Per search | Allocated per pass |
|---|---|---|---|
| Before (HashSet visited, per-call route) | 284.9 ms | 9.19 ms | 91.93 KB (2.97 KB per search) |
| After (generation stamp, scratch route) | 180.3 ms | 5.82 ms | 0 |

Time drops 37%, steady-state managed allocation per search is zero apart from the returned
waypoint list (the queue copies it once per async request under the lock). The corpus waypoint
lists are byte-identical before and after; the corpus lives at
`test/Panopticon.Simulation.Tests/Navigation/PathEqualityCorpus.json` with an Interactive
regeneration check (`RegeneratedCorpus_MatchesTheCommittedFixture`, deliberate regeneration via
`PANOPTICON_PATH_CORPUS_OUTPUT`). Diagonal-gap shapes stay covered by the synthetic wall cases
in `GraphTest`; WI-N1-02 later regenerated the fixture with two generator walls (33 cases
including 2 unreachable pairs) and re-verified equality.

WI-N1-02, connected-region labels, measured by the `RegionRelabel` benchmark on the same M0
grid (656x460, three regions after the corpus walls):

| Benchmark | Mean | Median | Allocated |
|---|---|---|---|
| Relabel (MarkDirty + next SameRegion) | 11.96 ms | 11.87 ms | 0 B |
| ColdRelabel (construct + first SameRegion) | 11.49 ms | 11.44 ms | 1.25 MB |
| UnreachableSearchFlood (cross-region Graph.Search) | 11.65 ms | 9.20 ms | 0 B |

One relabel costs about one avoided unreachable flood, so a single rejected request per dirty
period breaks even and additional rejections are pure savings. Labeling is lazy: relabeling
runs only on the first request after an `UpdateAreaPassability` mutation, never per request,
and the label array is the only allocation (cold, once). Guardrail verdict: relabeling does
not cost more than the floods it avoids. Corpus unreachable pairs (unreachable-01/02) now
return the same empty result with zero nodes expanded; `NavigationRegionsTests` verifies the
reject on a carved single-cell pocket (empty path, nodes expanded delta 0, failed search +1).

PathfindingLoad rerun (seed 1, deepdivide): the map is single-region so the region check never
rejects in-scenario and the unreachable case uses the off-map fallback, which aborts at
endpoint resolution before the region check; its span stays at noise level (baseline 0.12 ms,
rerun 0.25 ms, both 0 nodes). Reachable cases carry only the WI-N1-01 improvement:
squadless-1000 1376.4 to 1164.9 ms, squadless-100 247.7 to 191.0 ms, armyai-squads-1000
147.7 to 125.5 ms.

WI-N1-03, versioned passability snapshots, measured by the WI-B4-06 async anchor (same knobs:
capacity probe, deepdivide, 1000-request bursts every 300 frames, 14 bursts, bound 30, seed 1;
the anchor now records per-burst submit allocation because bursts land between B1 tick
windows). Before/after runs on consecutive commits of this branch:

| Measurement | Before (per-tick graph clone) | After (versioned snapshot) |
|---|---|---|
| Burst submit allocation (median / max) | 24.42 MB / 24.55 MB | 0.28 MB / 0.63 MB |
| Submit-to-apply wall (median / p99 / max) | 1093.3 / 2095.9 / 2166.5 ms | 1029.8 / 2007.6 / 2154.9 ms |
| Frames waited (median / max) | 20 / 43 | 18 / 42 |
| Deadline misses (of 14000) | 2171 | 1581 |

Steady-state submits allocate no graph: the only per-version cost is the 301,760-byte cell
array (combat mutates passability between most bursts, so a burst usually does pay one
rebuild). The first burst also bootstraps the worker graph once (0.63 MB). Submit-to-apply
waits are dominated by drain cadence during the contact phase, so they improve modestly while
the submit tick's 24 MB clone and its GC pressure disappear. Semantics: requests submitted
before a passability change compute against the old cells (`RequestsComputeAgainstSubmissionTimePassability`),
steady-state submits allocate under 1 MB on the 656x460 grid
(`SteadyStateSubmitsAllocateNoGraph`), and async results equal synchronous results across all
33 corpus cases served from one shared snapshot (`PathEqualityCorpusAsyncTests`). Anchor
artifacts: `/tmp/opencode/n1-03/` (before, before-instrumented, after reports and B1 records),
logs under `~/log/n1-03/`.

## WI-N2 results

WI-N2-01 (follower landing). Recorded 2026-10-24, seed 1, Release builds, on the post-main-merge
tree. All earlier N0/N1 scenario numbers in this document were Debug-build runs; the Release
re-baseline below supersedes them for comparisons. Artifacts: `/tmp/opencode/n2-01/`, logs under
`~/log/n2-01/`.

`NavigationOptions.MaxLandingRadiusCells` default 16. Landing-ring distribution over the whole
M4 army-AI probe (6471 landings): ring 1 4968, ring 2 1369, ring 3 7, ring 4 105, ring 5 22,
ring 6 and beyond 0. Every observed landing sits within ring 5; the 16-cell default covers the
distribution with headroom, and the measured default equals the shipped one.

PathfindingLoad (before = base without landing, after = landing on; spans are order-tick path
span):

| Case | Before span (ms) | After span (ms) | Before requests | After requests |
|---|---|---|---|---|
| squadless-1 | 1.50 | 1.52 | 1 unit | 1 unit |
| squadless-10 | 11.74 | 12.08 | 10 unit | 10 unit |
| squadless-100 | 89.63 | 90.85 | 100 unit | 100 unit |
| squadless-1000 | 511.44 | 573.79 | 1000 unit | 1000 unit |
| armyai-squads-1 | 0.83 | 0.84 | 1 unit | 1 unit |
| armyai-squads-10 | 1.45 | 1.47 | 2 leader | 2 leader |
| armyai-squads-100 | 4.13 | 4.20 | 13 leader | 13 leader |
| armyai-squads-1000 | 40.56 | 41.68 | 125 leader + 1 blocked follower | 125 leader + 1 landed, 0 fallback |
| squadless-10-impassable | 0.22 | 0.22 | 10 failed | 10 failed |

Squad-less cases are untouched by this card; their spread is run noise on identical request and
node counts (squadless-1000 expands 4128233 nodes both runs). The scenario's single blocked
follower (armyai-squads-1000) converts from a fallback search to a landing; nodes expand 434796
to 429519. Impassable offset-retrace points (recorded, fix is a follow-up per deep dive section
6): armyai-squads-100 2, armyai-squads-1000 130.

M4 army-AI probe (4500 frames, before = post-merge base, after = landing on):

| Metric | Before | After |
|---|---|---|
| Worst-tick path requests | 912 (248 squads + 664 blocked followers) | 253 (248 squads + 5 fallback, 662 landed) |
| Path span, nonzero ticks, mean / median (ms) | 85.9 / 69.5 | 58.2 / 52.1 |
| Path span, nonzero ticks, p99 / max (ms) | 242.8 / 466.6 | 167.2 / 181.1 |
| Path span, amortized over all ticks (ms) | 1.432 | 0.970 |

Budget verdicts:

| Budget | Value | Before | After | Verdict |
|---|---|---|---|---|
| `system.path.spanMs` amortized, M4 army-AI | at or under 1.7 ms | 1.432 ms | 0.970 ms | met at Release after the change |
| p99 on order ticks, M4 army-AI | at or under 16.7 ms | 242.8 ms | 167.2 ms | still unmet, about 10x over |
| Full searches per squad order | at or under squads plus fallback | 912 at the worst tick | 253 = 248 squads + 5 fallback | met exactly at the worst tick |

Invariants: unassigned 0 and commanderless 0 in both runs. The idle-stall check fails on the
post-merge base itself (sideA 2 units, max idle 2404 frames) and reads differently with landing
on (sideA 6 units, max idle 1110 frames); the base failure is attributed to merged mainline
content, since the pre-merge N1 head passes all checks on the same probe, and both base repeats
compare identical (the probe is deterministic per build). The landing shift changes squad
movement and therefore combat dynamics; the recorded impassable retrace points above are the
candidate mechanism and have a scheduled follow-up card.

WI-N2-02, group selection moves (Release, seed 1, PathfindingLoad, cluster cells 100; off = the
N2-01 state):

| Case | Off: span / requests | On: span / requests | Nodes off / on |
|---|---|---|---|
| squadless-10 | 11.71 ms / 10 unit | 4.12 ms / 3 leader + 1 unit | 63022 / 22084 |
| squadless-100 | 89.53 ms / 100 unit | 26.10 ms / 29 leader + 1 unit | 459909 / 132384 |
| squadless-1000 | 602.71 ms / 1000 unit | 80.54 ms / 108 leader + 1 unit | 4128233 / 427094 |
| squadless-10-impassable | 0.22 ms / 10 failed | 0.13 ms / 4 failed | 0 / 0 |

Army-AI squad cases are unchanged (hierarchy squads bucket before the cluster path). No blocked
followers occur in the squadless cases (open-field formation offsets stay passable), so landing
and fallback counters stay zero. Searches per squadless order at or under clusters plus one
loner: met (109 searches for 108 clusters + 1 single in the 1000-unit case).

The 1000-unit order-tick span moves from 602.71 ms (18x over the 33.3 ms one-frame budget) to
80.54 ms (2.4x over) at the default cluster size; the budget question moves to the N3 gate.

Cluster-size sensitivity, same scenario, `SelectionClusterCells` 200: squadless-1000 falls to
33.90 ms over 36 leader searches (146,956 nodes), 1.02x the 33.3 ms budget; squadless-100 is
16.31 ms. This is gate evidence: N2 at a wider cluster cell reaches the one-frame budget on the
N0 case without shared-destination fields.

## WI-N3-01 gate decision

Recorded 2026-10-25, seed 1, Release builds, deepdivide 656x460. Goal histogram added to
`PathStatistics` (WI-N3-01): every `ComputePath` that reaches `Graph.Search` buckets its goal
cell into 8-cell neighbourhood squares; the harness reads the window as a per-tick gauge.
Threshold: `GoalFieldMinRequests` 32 full searches sharing one neighbourhood on a single tick,
combined with an order-tick span budget miss. Artifacts: `/tmp/opencode/n3-01/`, logs under
`~/log/n3-01/`.

PathfindingLoad, group moves on (cluster 100), async off; goal max is the largest bucket on the
order tick:

| Case | Order-tick span (ms) | Goal max | Full searches | Budget |
|---|---|---|---|---|
| squadless-100 | 31.99 | 30 | 30 | under 33.3 ms |
| squadless-1000 | 81.76 | 109 | 109 | 2.5x over 33.3 ms |
| armyai-squads-1000 | 39.71 | 125 | 125 | 1.2x over 33.3 ms |
| squadless-10-impassable | 0.14 | 0 | 0 | rejected before search |

M4 army-AI probe (army AI on, 4500 frames, async off): worst decision tick 253 requests (248
squads + 5 fallback), of which 128 share one goal neighbourhood; path span nonzero ticks (75)
mean 57.7 / median 51.9 / p99 156.9 / max 172.7 ms; amortized 0.961 ms. The run fails the known
idle-stall check documented under WI-N2 results (pre-existing base issue, unrelated to this
card).

Gate legs after N2 with async off:

| Leg | Threshold | Measured | Holds |
|---|---|---|---|
| Sharing on one tick | at least 32 in one goal neighbourhood | 128 (M4 worst tick), 109 (squadless-1000 order tick) | yes |
| Order-tick span budget | miss | squadless-1000 81.76 ms vs 33.3; M4 p99 156.9 ms vs 16.7 | yes (miss) |

The authored gate opens on its own terms: after N2, same-goal full searches concentrate far past
the threshold and the order-tick spans miss every budget. The remedy it anticipated is already
shipped: N4 moves exactly these searches off the logic thread. PathfindingLoad with group moves
and async order paths both on measures 0.00 ms order-tick span on every case (429 submissions,
0 deadline misses, wait frames median 2 / max 11 against the bound 30; worker search time 225.8
ms total, off the logic thread). The M4 probe with the option on passes all checks with a 0.000
ms path span on all 4500 ticks (WI-N4 results). All authored `system.path.spanMs` budgets are
met with N2 plus N4 enabled; the shared-goal work remains as worker time with no budget over it.

Verdict: gate closed, no goal field is built. The shared-destination concentration is real and
measured (109 searches toward one destination in the 1000-unit selection; 128 on the M4 worst
decision tick), but a `GoalFieldCache` would only reduce worker-thread work that misses no
budget. Revisit if a consumer requires synchronous order paths, or if sustained same-goal
bursts push async wait frames past the 30-frame bound. N3 is Done, gate closed; no `WI-N3-02`
card is authored.

## WI-N4 results

WI-N4-02 (async order paths). Recorded 2026-10-25, seed 1, Release builds. Off = `AsyncOrderPaths`
false (the shipped default), on = true. Artifacts: `/tmp/opencode/n4-02/`, logs under `~/log/n4-02/`.

PathfindingLoad (spans are order-tick path span; requests columns show the request mix):

| Case | Off span (ms) | On span (ms) | Off requests | On async submitted |
|---|---|---|---|---|
| squadless-1 | 1.52 | 0.00 | 1 unit | 1 |
| squadless-10 | 12.23 | 0.00 | 10 unit | 10 |
| squadless-100 | 90.31 | 0.00 | 100 unit | 100 |
| squadless-1000 | 566.43 | 0.00 | 1000 unit | 1000 |
| armyai-squads-1 | 0.78 | 0.00 | 1 unit | 1 |
| armyai-squads-10 | 1.41 | 0.00 | 2 leader | 2 |
| armyai-squads-100 | 4.08 | 0.00 | 13 leader | 13 |
| armyai-squads-1000 | 40.01 | 0.00 | 125 leader | 125 |
| squadless-10-impassable | 0.22 | 0.00 | 10 failed | 10 |

With the option on every case reports a zero logic-thread `path` span and zero synchronous requests;
the whole run submitted 1402 async requests, applied 1317, and recorded zero deadline misses against
the 30-frame bound, with wait frames median 12, p99 29, max 29. Worker-thread search time totalled
974.78 ms across the run and is reported separately (`WorkerSearchMs`), not inside the logic span.
Failed searches still surface: the impassable case reports its 10 failed searches through the async
path. Per-case async counts above are the requests drained within the first window tick.

M4 army-AI probe (4500 frames, off = same tree with the option off):

| Metric | Off | On |
|---|---|---|
| Logic `path` span, all ticks | mean 0.980 ms, p99 52.3, max 177.3 | 0.000 ms on every tick |
| Worst-tick synchronous path requests | 253 (248 squads + 5 fallback) | 0 |
| Async submitted / applied / cancelled | 0 / 0 / 0 | 18689 / 18687 / 2 |
| Wait frames, median / p99 / max | n/a | 1 / 6 / 8 |
| Deadline misses vs 30-frame bound | n/a | 0 |
| Idle-stall check | fail (sideA 6 units idle over 600 frames) | pass |

About 18700 async submissions over 75 decision ticks match the off-run's synchronous request volume;
the 2 cancellations are superseded orders. With the option on, the B1 per-tick async counters read
zero because worker threads finish searches between the probe's tick snapshots; the lifetime queue
totals above are the authoritative counts. Worker search time is not part of the M4 record (the
probe samples per-tick only).

Verdict: the WI-N4 goal is met. With `AsyncOrderPaths` on, order-tick logic-thread path span drops
to submission cost (measured zero across every scenario and probe tick), completions land inside the
frame bound with deadline misses 0, and the M4 stall and squad invariants hold in the on-run. The
off-run stall failure is the same pre-existing base issue recorded under WI-N2 results:
option-off movement still includes the N2 landing change, which shifts which units the idle check
flags, while the on-run passes the check outright. Aircraft, dozer, supply, worker, and script
movement stay synchronous per the guardrails; option-off behaviour is unchanged.

## WI-N5-01 verification

Pending.
