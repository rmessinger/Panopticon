# Track N: Multi-unit pathfinding performance and deadlocks

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
| [`WI-N5-01`](../agent/cards/WI-N5-01-scale-verification.md) | N5 | Scale verification | N5-1 | Done |

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

Recorded 2026-10-25, seed 1, Release builds, same host as every Track N run, deepdivide 656x460.
Both options on: `GroupSelectionMoves` (cluster 100) and `AsyncOrderPaths`. The M4 army-AI probe
has no group-moves knob and army AI routes through hierarchy squads before clustering, so the M4
on-runs carry `AsyncOrderPaths` only. Artifacts: `/tmp/opencode/n5-01/` (on-runs; the off A/B run
is `/tmp/opencode/n3-01/m4-report.json` at the same commit), logs under `~/log/n5-01/`.

PathfindingLoad with both options on: every case reports a 0.00 ms order-tick `path` span and
zero synchronous requests; 429 submissions applied 429 with 0 cancellations, 0 deadline misses,
wait frames median 2 / p99 8 / max 11 against the bound 30; worker search time 225.8 ms total,
off the logic thread. `tick.wallMs` is unchanged (median 1.82 off and on; bootstrap CIs overlap
at [1.71, 1.84] versus [1.71, 1.90]), so submission cost is invisible at scenario scale. The
1000-unit selection issues 109 submissions (108 cluster leaders plus 1 unclustered single, 0
fallback searches); the army-AI 1000 case issues 125 leader submissions with 5 followers served
by landing. The impassable case still reports its 10 failed searches through the async path.

M4 army-AI probe, 4500 frames, off versus on at the same commit (on-run 1 / on-run 2):

| Metric | Off | On |
|---|---|---|
| Logic `path` span | nonzero on 75 ticks: mean 57.7, median 51.9, p99 156.9, max 172.7 ms; amortized 0.961 ms | 0.000 ms on every tick, both runs |
| Worst-tick synchronous requests | 253 (248 squads + 5 fallback) | 0 |
| Async submitted / applied / cancelled | 0 | 18780 / 18780 / 0 and 18762 / 18761 / 1 |
| Async deadline misses, wait frames median / max | n/a | 0 both runs; 1 / 8 and 1 / 9 |
| `tick.wallMs` mean / median / p99 / max | 19.50 / 17.78 / 70.22 / 204.39 ms | 17.84 / 17.18 / 27.11 / 42.63 ms |
| Squad invariants (unassigned, commanderless) | 0, 0 | 0, 0 both runs |
| Idle-stall check | fail: sideA 6 units, max idle 1110 | fail: sideB 2 units (max idle 2900) and sideB 3 units (max idle 2584) |

Budget verdicts (deep dive section 3, measured with options on):

| Budget | Value | Measured | Verdict |
|---|---|---|---|
| `system.path.spanMs` amortized median, M4 | at or under 1.7 ms | 0.000 ms | Met |
| `system.path.spanMs` p99 on order ticks, M4 | at or under 16.7 ms | 0.000 ms | Met |
| `path` span on a 1000-unit selection move tick | at or under 33.3 ms | 0.00 ms (109 async submissions) | Met |
| Full searches per multi-unit order | at most groups plus region-crossing followers | 109 = 108 clusters + 1 single, 0 fallback; 125 leaders + 5 landed | Met |

Engagement checks: armies engage and trade (60 and 59 units destroyed across the on-runs), async
completions stay inside the frame bound with zero misses, and squad invariants hold. The
idle-stall check fails at this commit in both modes (off: sideA 6 units; on: sideB 2 to 3 units
across two runs, after the N4-02 on-run passed it); max-idle values 1110 to 2900 sit in the band
of the pre-existing base failure documented under WI-N2 results (base max idle 2404, landing-on
1110), which the pre-merge N1 head does not reproduce. Per the card, this failure is reported and
stops further verification: it is the scheduled base-stall follow-up, not an N-options
regression, and every pathfinding budget above is measured Met regardless.

Default-flip recommendation (operator decides): keep both defaults off. `AsyncOrderPaths` meets
every budget and smooths decision ticks (`tick.wallMs` p99 70.2 to 27.1 ms), but it introduces
the accepted run-to-run nondeterminism and the base-stall follow-up should land first so the
on-mode invariant can hold run over run. `GroupSelectionMoves` additionally changes
player-visible movement (cluster followers retrace leader paths), so it stays opt-in until the
order UX is reviewed against per-unit paths.

## Navigation deadlocks (N6-N10)

Source of truth: [deep-dives/n-navigation-deadlocks.md](../deep-dives/n-navigation-deadlocks.md) :
[Architecture diagrams §8](../deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams).

| Card | Phase | Title | Checklist item | Status |
|---|---|---|---|---|
| [`WI-N6-01`](../agent/cards/WI-N6-01-movement-progress-and-stall-attribution.md) | N6 | Movement progress and stall attribution | N6-1 | Done |
| [`WI-N6-02`](../agent/cards/WI-N6-02-navigation-deadlock-scenario.md) | N6 | `NavigationDeadlock` scenario and baseline | N6-2 | Done |
| [`WI-N7-01`](../agent/cards/WI-N7-01-async-path-liveness.md) | N7 | Async and squad path liveness | N7-1 | Done |
| [`WI-N7-02`](../agent/cards/WI-N7-02-offset-route-validation.md) | N7 | Offset route validation behind an option | N7-2 | Done |
| [`WI-N8-01`](../agent/cards/WI-N8-01-blocked-wait-and-local-repath.md) | N8 | Blocked wait and bounded local repath | N8-1 | Done |
| [`WI-N8-02`](../agent/cards/WI-N8-02-give-way-and-mutual-block.md) | N8 | Give-way and mutual block yield | N8-2 | Done |
| [`WI-N9-01`](../agent/cards/WI-N9-01-destination-slots.md) | N9 | Destination slots behind an option | N9-1 | Done |
| [`WI-N9-02`](../agent/cards/WI-N9-02-crowd-arrival.md) | N9 | Crowd arrival | N9-2 | Done |
| [`WI-N10-01`](../agent/cards/WI-N10-01-deadlock-verification.md) | N10 | Deadlock verification | N10-1 | Done |

Delivery: stacked group PRs into `main` via the `gh stack` extension (superseded the staging model Oct 2026). Suggested groups:
1 = N6, 2 = N7, 3 = N8, 4 = N9, 5 = N10. Groups 2 to 4 may stack in any order after group 1.

## Navigation deadlocks results

### WI-N6-01 baseline (2026-10-27, Release, seed 1, deepdivide 656x460, 4500 frames)

Stall counting switched from the WI-M4-06 route-state rule to the progress rule (WI-N6-01).
The old rule counted a unit holding target points as engaged and never registered jams; the
new rule flags units holding an order with more than 600 frames without waypoint progress, or
a NoRoute order. Counts rise by design; these tables are the new baseline, not a regression.

| Metric (M4 army-AI probe) | Options off | AsyncOrderPaths on |
| --- | --- | --- |
| Stalled units at end (sideA / sideB) | 780 / 420 | 960 / 954 |
| Max frames without progress | 4436 | 4435 |
| Stall records: BlockedByUnit | 1728 | 1865 |
| Stall records: reason None (no blocker identified) | 270 | 132 |
| Wait-for cycles (latest sample) | 76 | 93 |
| Worst-tick sync path requests | 253 | 0 |
| Async applied / deadline misses | 0 / 0 | 18744 / 0 |

Attribution, options off: the worst stalls are Ranger columns stuck nearly the whole run
(frames 4400+) with adjacent unit ids blocking each other; the sampled wait-for cycles are
mutual pairs, the head-on mechanism of deep dive section 1. Units with reason None hold
orders without waypoint progress and without a footprint blocker in the swept probe: the
crowd-wait pattern behind arrived units, which is N9 evidence, and no units reached the
600-frame threshold with NoRoute. The pre-N6 idle-stall finding (2 to 6 stalled units under
the route-state rule) is subsumed by this baseline: those units appear inside the counts
above. Both modes still fail the idle-stall check (exit 5); the check itself moves to the
deep dive section 3 target of zero-progress units within 600 frames, to be met by N7 to N9.

Artifacts: `/tmp/opencode/n6-01/m4-{off,on}-report.json`, logs `~/log/n6-01/`.

### WI-N6-02 baseline (2026-10-27, Release, seed 1, alpine assault, 3600-frame budget)

NavigationDeadlock scenario, six cases, each run to settle or budget. All six mechanisms
reproduce; five of six cases settle nothing inside the budget, confirming the deep dive
section 1 finding that blocked ground units have no timeout, repath, or sidestep.

| Case | Settled | Frames | Stalls by reason | Cycles | Span ms | Mechanism |
| --- | --- | --- | --- | --- | --- | --- |
| head-on-pair | 0 / 2 | 3600 | BlockedByUnit 2 | 1 | 9.6 | Confirmed |
| corridor-opposed-20 | 0 / 40 | 3600 | BlockedByUnit 40 | 4 | 11.8 | Confirmed |
| crowd-goal-100 | 3 / 100 | 3600 | BlockedByUnit 99, None 1 | 1 | 20.1 | Confirmed |
| queue-behind-idle | 0 / 10 | 3600 | BlockedByUnit 10 | 0 | 4.4 | Confirmed |
| async-no-route | 5 / 6 | 3600 | NoRoute 5, AwaitingPath 1 | 0 | 4.8 | Confirmed |
| squad-offset-retrace | 2 / 8 | 3600 | BlockedByUnit 6, None 2 | 0 | 4.9 | Confirmed |

Case notes:

- head-on-pair: two tanks ordered through each other's start block every frame from the
  first contact; 3487 frames without progress at the end. One wait-for cycle (the pair).
- corridor-opposed-20: both columns jam at the 120-wide gap; 4 cycles among column heads.
- crowd-goal-100: 100 squad-less tanks onto one point; 3 reach the goal radius, 99 hold
  blocked records. The reason-None record is the crowd-wait pattern behind arrived units
  (N9 evidence, same as the M4 reason-None population).
- queue-behind-idle: a single-file column through the gap held by two idle tanks; at least
  one record blames an idle tank directly (blocker chain into the parked pair).
- async-no-route: sealed pocket (hollow annulus of power plants, pitch 30) rejects 5 async
  requests that settle as NoRoute at frame 4 after drain; an injected worker failure at
  frame 90 strands the victim in AwaitingPath for the remaining 3511 frames. Two N7
  findings: a worker exception completes the request as Cancelled without a callback, and
  such a request also blocks drain application behind it (head-of-line), observed when the
  failure was injected at submit time: 5 submitted, 0 applied, all five units awaiting for
  the whole budget.
- squad-offset-retrace: 2 of 8 followers hold retrace points on impassable cells
  (impassableRetracePoints 2), the WI-N2-01 residual this case isolates.

Verdicts against the section 3 targets: the case-settling target and the zero-progress
target are unmet everywhere except the NoRoute settle latency (the drain delivers the
pocket failure within 4 frames, well inside the 2x AsyncPathFrameBound watchdog target,
but that watchdog does not exist yet, N7). The wait-for-cycles target (0 at case end) is
unmet in 3 of 6 cases. Path spans stay far inside the N5 budgets (max 20.1 ms single tick
in crowd-goal-100, amortized below 0.01 ms). These targets stand as the acceptance gates
for N7 to N9.

Artifacts: `/tmp/opencode/n6-02/navigation-deadlock-report.json`, logs `~/log/n6-02/`.

### WI-N7-01 results (2026-10-27, Release, seed 1)

Failure-path liveness: a worker exception now completes the request as Failed (the drain
applies it and the requester callback runs), the queue head advances past it, and every
failure path settles the affected units to NoRoute instead of stranding them.

- NavigationDeadlock async-no-route case (alpine assault, budget 3600): 6 of 6 settled at
  frame 92 (pre-N7-01: 5 of 6, victim AwaitingPath 3511 frames). Async 6 submitted, 6
  applied, 0 cancelled; 6 NoRoute records, 0 units still awaiting at end. The worker
  failure at frame 90 settles the victim through the Failed callback within one drain.
- M4 army-AI probe, AsyncOrderPaths on, deepdivide, 4500 frames: async 18707 submitted,
  18707 applied, 0 cancelled, 0 deadline misses, wait frames median 1 / p99 6 / max 9,
  worst-tick sync requests 0. Stall records: 1906 BlockedByUnit + 91 reason None,
  0 AwaitingPath. Idle-stall check still fails (sideA 968, sideB 961 stalled at end,
  max 4434 frames without progress): the remaining stalls are unit-unit blocking (N8/N9
  scope), not path liveness.
- Guardrail confirmation: no watchdog fallback fired in the M4 run (0 cancelled, no extra
  sync requests), so the PendingPathTimeoutFrames path only costs anything after a real
  stall, and successful-path behavior is unchanged (wait frames and totals match the
  N6-01 on-run).

Artifacts: `/tmp/opencode/n7-01/`, logs `~/log/n7-01/`.

### WI-N7-02 results (2026-10-28, Release, seed 1)

Offset route validation (`NavigationOptions.ValidateOffsetRoutes`, default off) walks each
formation-offset retrace cell by cell (Bresenham segments plus a waypoint check; waypoints
landing on impassable cells are caught even though segment walks skip endpoints) and reroutes
crossing followers onto the leader route through the N2-01 landing ring, or to their own
search when landing is disabled or fails.

- NavigationDeadlock full run with the option on (alpine assault, budget 3600):
  impassableRetracePoints 0 in every case (baseline: squad-offset-retrace 2). The
  squad-offset-retrace case rerouted 7 of 7 followers onto the leader route, 0 retrace
  points, mechanism confirmed as remediated. Other cases unchanged (their retrace counts
  were already 0).
- PathfindingLoad with the option on (deepdivide, army-AI squad cases): armyai-squads-1000
  rerouted 737 followers (738 landed, 0 fallbacks), searches per order unchanged at the 125
  squad leaders, order-tick span 41.74 ms vs 41.68 baseline (noise; the Crosses walk is
  cheap against the search cost). armyai-squads-100: 81 rerouted; armyai-squads-10: 8.
  Rerouted followers ride the leader route without a new search, so the N2 bound holds
  without the plus-rerouted allowance.
- M4 army-AI probe with AsyncOrderPaths and ValidateOffsetRoutes both on, deepdivide,
  4500 frames: async 18611 submitted / 18611 applied / 0 cancelled, 0 deadline misses,
  wait median 1 / p99 5 / max 7, worst-tick sync requests 0. Stall profile unchanged
  (1967 BlockedByUnit + 30 reason None vs 1865 + 132 in the N7-01 on-run; cycles 47 vs 93):
  deepdivide stalls are unit-unit blocking (N8), offset retraces contribute nothing there.
  Per-decision-tick landed/fallback/rerouted counters read 0 in async mode because worker
  callbacks land after the sampler's one-frame burst window; async request totals are the
  authoritative counters.
- Mass rerouting on deepdivide (737 of ~875 followers) collapses formation spread onto the
  leader route when offsets cross cliff cells. That is the option's designed trade, recorded
  for the N10 default-flip review.

Artifacts: `/tmp/opencode/n7-02/`, logs `~/log/n7-02/`.

### WI-N8-01 results (2026-10-28, Release, seed 1)

Blocked-unit resolution (`NavigationOptions.BlockedUnitResolution`, default off) waits
`BlockedWaitFrames` (15) frames without progress behind a non-advancing blocker, then splices a
bounded local detour: a `(2W+1)^2` cell window (`LocalRepathWindowCells` 12) around the unit,
passability plus stationary-unit occupancy, rejoin at the first waypoint outside the window
clamped into it, at most `MaxResolutionAttempts` (3) attempts per blocked stretch, then give up
and settle `GoalCrowded` when the goal is within `CrowdArrivalRadius` (60) or keep the route and
report. Advancing blockers are waited out; attempts reset on any progress.

- Install-free tests (synthetic 16x16): a unit blocked by a stationary registered unit splices a
  detour around the blocked cell and preserves its goal; expansion is bounded by the window
  square (81 cells at W 4); the attempt cooldown and advancing-blocker wait hold; give-up
  settles GoalCrowded within the arrival radius and keeps the route beyond it.
- NavigationDeadlock with the option on (alpine assault, budget 3600): repaths fire in every
  blocking case (head-on-pair 8, corridor-opposed-20 143, crowd-goal-100 352, queue-behind-idle
  33, squad-offset-retrace 22; async-no-route 0) but no case settles that did not settle before.
  Mutual pairs oscillate: a detour makes progress, attempts reset, the pair re-blocks (head-on
  max frames 3471). Columns exhaust attempts against the queue head and keep their routes
  (queue-behind-idle 0/10). crowd-goal-100 converts its crowd-wait into 1 GoalCrowded settle
  plus 352 repaths, still 3/100. This matches the deep-dive split: local repath relieves
  individual jams, give-way (WI-N8-02) resolves standing mutual and queue deadlocks.
- M4 army-AI probe with AsyncOrderPaths and BlockedUnitResolution on, deepdivide, 4500 frames:
  path.blockedRepathPerTick median 1 (30 per second), system.path.spanMs median 0.059 ms,
  tick.wallMs median 16.5, async 18704/18703/1, 0 deadline misses, wait 1/4/7. N5 budgets hold.
  Stalls remain (954/976 at end; records 1961 BlockedByUnit + 14 GoalCrowded + 23 reason None,
  cycles 52 vs 93 in the N7-01 on-run): mass combat blocking needs the give-way leg; the
  reason-None crowd-wait records dropped from 91 to 23.
- Option off changes nothing (default-off; the resolver only runs behind the option check and
  the progress trigger).

Artifacts: `/tmp/opencode/n8-01/`, logs `~/log/n8-01/`.

### WI-N8-02 results (2026-10-28, Release, seed 1)

Give-way extends `BlockedUnitResolution` (same option, no new knob) with the deep-dive step 2: a
friendly idle or order-less blocker is asked to sidestep (`AIUpdate.RequestGiveWay`, recording the
requester in `_moveOutOfWay1`); `GiveWayPlanner.FindSidestep` picks the nearest free passable cell
in the local window that clears the requester's next segment by both radii plus padding plus a
10.0f re-block margin. Mutual blocks yield by the higher object id; the lower unit waits, but its
wait expires after twice `BlockedWaitFrames` without blocker activity, and every failed give-way
falls through to the local repath instead of looping. One sidestep per blocker per wait window.

- Install-free tests (synthetic 16x16): mutual pair, the higher id sidesteps and the lower waits;
  an idle friendly blocker sidesteps exactly once (no cascade within the window); an enemy
  blocker never gives way and the requester repaths; a busy friendly blocker refuses and the
  requester repaths. Navigation and GroundFootprint filters 99/99.
- NavigationDeadlock with the option on (alpine assault, budget 3600, final run): head-on-pair
  settles 2/2 at frame 367 with 0 cycles and 1 repath (the yield plus the expired-wait repath
  resolves the pair; was 0/2 forever at N8-01); queue-behind-idle settles 6/10 with 0 cycles (was
  0/10; the idle tanks give way and the column flows); crowd-goal-100 reaches 9/100 (was 3);
  corridor-opposed-20 remains 0/40 (two 20-columns through one 120-wide gap is a capacity
  problem, recorded as unmet rather than tuned away); async-no-route and squad-offset-retrace
  unchanged (6/6 at frame 93, 2/8).
- M4 army-AI probe with AsyncOrderPaths and BlockedUnitResolution on, deepdivide, 4500 frames:
  stalls 729/626 at end (N8-01: 954/976), cycles 29 (was 52), records 1616 BlockedByUnit + 380
  reason None + 2 GoalCrowded; give-way converts blocked-stalls into crowd-waits (reason None up
  from 23). path.blockedRepathPerTick median 17 (510 per second, up from 1: unjammed units
  re-block in mass combat), system.path.spanMs median 1.644 ms, tick.wallMs median 19.1; the
  amortized N5 span budget still holds, order-tick spans remain asynchronous.
- Option off changes nothing.

Artifacts: `/tmp/opencode/n8-02/`, logs `~/log/n8-02/`.

### WI-N9-01 results (2026-10-26, Release, seed 1)

Destination slots (`NavigationOptions.DestinationSlots`, default off) give squad-less
selections per-unit goals: `DestinationSlots.Assign` packs rings outward from the order point
by footprint radius (ring step from the largest radius, 16-cell bound from
`DestinationSlotCells` since Generals parses `MaxCellsAdjustDestination` as 0), passable cells
in the destination's region, deterministic by distance then object id. Squads keep formation
offsets either way; past the bound or off-region the shared point remains.

- Install-free tests (synthetic 16x16): slots are unique, passable, in-region, packed outward
  in non-decreasing distance, deterministic across input orderings, fall back past the bound,
  and `MoveUnits` spreads a squad-less selection while the option off keeps the shared point
  (5/5).
- NavigationDeadlock with the option on (alpine assault, budget 3600): crowd-goal-100
  4/100 settled (baseline 3/100). The gain is marginal by geometry: 100 Crusader footprints
  cannot pack inside a 16-cell bound (ring capacity at 50-unit spacing is ~38 tanks), so most
  of the crowd still presses on the packed disk. The blocked records drop from 99 to 98 with 2
  unattributed crowd-waits. Other cases are untouched by slots alone (head-on 0/2, corridor
  0/40 with 7 cycles, queue 0/10: this run has only the slots option on).
- PathfindingLoad with the option on (deepdivide): searches per order unchanged (squadless
  rows stay at one unit request per unit, army-AI rows at squad leaders; the N2 bound holds
  trivially). Squadless-1000 order-tick span 601.13 ms vs 536.24 baseline (+12 percent,
  different goal cells); armyai-squads-1000 42.84 vs 41.74 ms (noise).
- Option off changes nothing.

Artifacts: `/tmp/opencode/n9-01/{nd,pl}-slots.json`, logs `~/log/n9-01/`.

### WI-N9-02 results (2026-10-26, Release, seed 1)

Crowd arrival: a unit whose whole remaining route sits inside the arrival disk around its
goal, blocked for `BlockedWaitFrames` by an arrived friendly (no route, not moving, same
owner) standing inside `CrowdArrivalRadius` of the goal, clears its route and reports
`CrowdArrived`. Active when `BlockedUnitResolution` or `DestinationSlots` is on. The radius
default moved from the N8-01 placeholder 60 to 250, taken from the WI-N6-02 crowd
distribution (100 Crusader footprints pack into a ~225-unit disk); the GoalCrowded give-up
shares the radius and settles more willingly as a result.

- Install-free tests (synthetic 16x16): settle fires behind an arrived friendly near the
  goal; never for a reachable goal (blocker outside the radius), a moving or enemy or ordered
  blocker, a multi-segment route reaching outside the disk, or with both gate options off
  (4/4). Crowd-arrival tests + full Navigation filter pass.
- crowd-goal-100 (alpine assault, budget 3600): slots off (resolution gate) 16/100 settled
  with 2 CrowdArrived records; slots on 17/100 with 3 CrowdArrived plus 3 GoalCrowded
  (baseline 3/100). The acceptance criterion of 0 units holding routes is unmet and recorded:
  the crowd front settles behind arrived friendlies, but the pile interior is mutually
  blocked ordered units that neither arrive nor block on arrived units; dissolving that
  interior needs the corridor-class capacity work outside N9 scope. 85 (slots off) and 82
  (slots on) units still hold blocked routes at budget end.
- Side effects of the 250 radius on the other cases (full runs): queue-behind-idle 9/10
  settled with resolution only (was 6/10 at radius 60; wider GoalCrowded give-up and crowd
  arrival settle units the column jam held); squad-offset-retrace 4/8 (was 2/8, 2 GoalCrowded
  settles); head-on 2/2 at frame 367 and corridor 0/40 unchanged; async-no-route unchanged.
  With slots added, queue drops to 5/10 and corridor settles its first unit (1/40): option
  interplay, recorded for the N10 review.
- Option off changes nothing.

Artifacts: `/tmp/opencode/n9-02/`, logs `~/log/n9-02/`.

### WI-N10-01 verification (2026-10-26, Release, seeds 1-3)

Twenty-four runs: every `NavigationDeadlock` case and the M4 army-AI probe at seeds 1, 2, and 3
with all Track N options on, then each of `ValidateOffsetRoutes`, `BlockedUnitResolution`, and
`DestinationSlots` off in turn (`AsyncOrderPaths` on throughout). The NavigationDeadlock cases
are seed-stable (identical numbers at all three seeds; deterministic by construction), so case
numbers below cite one seed; M4 numbers vary by seed and cite ranges. The M4 idle-stall check
fails in every run (exit 5), the documented pre-existing base finding.

Deep dive section 3 verdicts:

| Target | Verdict | Numbers (all options on) |
| --- | --- | --- |
| Every deadlock case settles in budget | Missed | head-on-pair 2/2 at 367 and async-no-route 6/6 at 92-93 settle; corridor-opposed-20 1/40, crowd-goal-100 17/100, queue-behind-idle 5/10, squad-offset-retrace 0/8 do not |
| M4 zero-progress units none over 600 frames, 3 seeds | Missed | max frames without progress 3401-4040 per side per seed; stalled at end 355-956 per side |
| Wait-for cycles 0 at case end | Missed | 4 of 6 cases read 0 (head-on, queue, async, squad-offset); corridor 2, crowd 8 |
| No-route settle latency | Met | pocket failure settles 6/6 at frames 92-93 in every config and seed; the drain delivers the failure within 4 frames of the order, inside the 60-frame watchdog bound |
| Resolution cost keeps N5 budgets | Met | M4 `system.path.spanMs` medians 0.537-1.644 ms, `tick.wallMs` 17-21 ms, async 0 deadline misses in all 12 runs; the (2W+1)^2 repath bound is pinned by the N8 tests |

Residual causes for the misses: corridor-opposed-20 pushes 40 tanks through one 120-unit gap
(capacity, not liveness); crowd-goal-100's interior holds mutually blocked ordered units whose
goals are other units' positions (arrival semantics terminate 17 and convert 3 to
`CrowdArrived`, but the pile cannot drain through the ring); queue-behind-idle settles the
column head through give-way while the tail's mutually blocked units settle only partially
(5/10 with slots, 9/10 without); squad-offset-retrace with validation on reroutes all followers
onto the leader route, where they bunch at the wall end (0/8; without validation 4/8). M4's
stalled population is combat-locked attack-move clusters: attack-move pauses freeze the
progress counter while the units fire, and mass mutual blocking of ordered units is not a
path-liveness failure.

Option isolation (NavigationDeadlock, seed-stable; M4 3-seed ranges):

- BlockedUnitResolution off regresses every case to the N6 baseline: head-on 0/2 with 1 cycle,
  queue 0/10, corridor 0/40 with 7 cycles, crowd 4/100, no repaths anywhere; M4 cycles rise to
  40-54 (16-31 on) and stalled units reach 983. It is the load-bearing option.
- DestinationSlots off costs the corridor its single settler (0/40) and crowd one settle
  (16/100), but improves queue-behind-idle to 9/10 (slotted columns interleave worse behind
  the gap); M4 deltas stay inside seed noise. Mixed value.
- ValidateOffsetRoutes off changes only squad-offset-retrace among the cases: 4/8 settled with
  22 repaths versus 0/8 with 231 (validation packs followers onto the leader route and they
  bunch at the wall end); M4 cycles trend slightly better with it on (16-31 vs 22-34). The
  N7-02 retrace-point elimination stands.

Default-flip recommendation for the operator:

- BlockedUnitResolution: flip on. Decisive deadlock relief (head-on and queue mechanisms
  remediated, M4 cycles halved), bounded cost (span medians under 1.7 ms, window-bound
  repaths).
- AsyncOrderPaths: flip on for gameplay runs. Zero deadline misses and wait frames 1/4-8
  across all 12 probe runs since the N7 liveness work; keep off only for byte-determinism
  baselines, where completion frames legitimately vary.
- DestinationSlots: keep off by default. Crowd spreading gains one settle and the corridor's
  first settler, at a measured queue regression (9/10 to 5/10) and M4 noise. Operators may
  enable it for crowd-heavy scenarios.
- ValidateOffsetRoutes: keep off by default. It removes impassable retraces (the N7-02 goal)
  but collapses formation spread on cliff-hugging paths and costs squad-offset settles;
  enable where offset retraces across obstacles are observed.
- GroupSelectionMoves: unchanged by N6-N10 (not part of the deadlock matrix); the N5 verdict
  (budgets met, optional) stands.

Artifacts: `/tmp/opencode/n10-01/` (24 runs), logs `~/log/n10-01/`.

## Confined passage queueing (N11-N14)

Source of truth: [deep-dives/n-confined-passage-queueing.md](../deep-dives/n-confined-passage-queueing.md) :
[Architecture diagrams §8](../deep-dives/n-confined-passage-queueing.md#8-architecture-diagrams).

| Card | Phase | Title | Checklist item | Status |
|---|---|---|---|---|
| [`WI-N11-01`](../agent/cards/WI-N11-01-passage-finder.md) | N11 | Passage finder and cross-section cache | N11-1 | Ready |
| [`WI-N11-02`](../agent/cards/WI-N11-02-passage-cases-and-baseline.md) | N11 | Passage cases and option-off baseline | N11-2 | Blocked |
| [`WI-N12-01`](../agent/cards/WI-N12-01-passage-coordinator-and-queue.md) | N12 | Passage coordinator, queue order, and lane path | N12-1 | Blocked |
| [`WI-N12-02`](../agent/cards/WI-N12-02-passage-hold-and-release-movement.md) | N12 | Passage hold, release, and route splice | N12-2 | Blocked |
| [`WI-N13-01`](../agent/cards/WI-N13-01-opposing-flow-arbitration.md) | N13 | Opposing flow arbitration | N13-1 | Blocked |
| [`WI-N13-02`](../agent/cards/WI-N13-02-group-passage-entry.md) | N13 | Squad and cluster passage entry | N13-2 | Blocked |
| [`WI-N14-01`](../agent/cards/WI-N14-01-passage-verification.md) | N14 | Passage queueing verification | N14-1 | Blocked |

Delivery: phase staging branch `staging/track-n-passages`, numbered group PRs. Suggested groups:
1 = N11, 2 = N12, 3 = N13, 4 = N14. `WI-N11-01` may start before N6; `WI-N11-02` waits on
`WI-N6-02`.

## Confined passage queueing results

Pending `WI-N11-01`.

## Layered navigation (N15-N20)

Source of truth: [deep-dives/n-layered-navigation.md](../deep-dives/n-layered-navigation.md) :
[Architecture diagrams §9](../deep-dives/n-layered-navigation.md#9-architecture-diagrams).

| Card | Phase | Title | Checklist item | Status |
|---|---|---|---|---|
| [`WI-N15-01`](../agent/cards/WI-N15-01-navigation-plan-layer-contract.md) | N15 | Navigation plan and layer contract | N15-1 | Blocked |
| [`WI-N15-02`](../agent/cards/WI-N15-02-layered-cases-and-baseline.md) | N15 | Layered cases, layer spans, and option-off baseline | N15-2 | Blocked |
| [`WI-N16-01`](../agent/cards/WI-N16-01-sector-graph.md) | N16 | Sector graph with incremental rebuild | N16-1 | Ready |
| [`WI-N16-02`](../agent/cards/WI-N16-02-strategic-planner.md) | N16 | Strategic planner and route-quality corpus | N16-2 | Blocked |
| [`WI-N17-01`](../agent/cards/WI-N17-01-corridor-search-and-stitching.md) | N17 | Masked corridor search and segment stitching | N17-1 | Blocked |
| [`WI-N17-02`](../agent/cards/WI-N17-02-layered-unit-main-path.md) | N17 | Layered unit main path behind `LayeredNavigation` | N17-2 | Blocked |
| [`WI-N18-01`](../agent/cards/WI-N18-01-navigation-group-and-spine.md) | N18 | Navigation groups and spine following | N18-1 | Blocked |
| [`WI-N18-02`](../agent/cards/WI-N18-02-width-adaptive-formation-and-passages.md) | N18 | Width-adaptive formation and passage handoff | N18-2 | Blocked |
| [`WI-N18-03`](../agent/cards/WI-N18-03-crowd-routing.md) | N18 | Crowd routing for large selections | N18-3 | Blocked |
| [`WI-N19-01`](../agent/cards/WI-N19-01-steering-core.md) | N19 | Reciprocal velocity-obstacle steering core | N19-1 | Ready |
| [`WI-N19-02`](../agent/cards/WI-N19-02-steering-integration.md) | N19 | Steering integration with N8 fallback | N19-2 | Blocked |
| [`WI-N20-01`](../agent/cards/WI-N20-01-layered-navigation-verification.md) | N20 | Layered navigation verification | N20-1 | Blocked |

Delivery: phase staging branch `staging/track-n-layers`, numbered group PRs. Suggested groups:
1 = N16 and `WI-N19-01` (pure, no movement change), 2 = N15, 3 = N17, 4 = N18, 5 = `WI-N19-02`,
6 = N20. Group 1 may start now; group 2 waits on `WI-X6-01`.

## Layered navigation results

Pending `WI-N16-01`.
