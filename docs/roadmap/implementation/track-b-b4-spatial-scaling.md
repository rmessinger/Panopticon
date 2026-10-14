# Track B · B4: Spatial and pathfinding scaling

Source of truth: [deep-dives/b4-spatial-pathfinding-scaling.md](../deep-dives/b4-spatial-pathfinding-scaling.md) :
[Architecture diagrams](../deep-dives/b4-spatial-pathfinding-scaling.md#architecture-diagrams).

Audience: human overview; agents load the cards.

| Card | Title | Checklist item |
|---|---|---|
| [`WI-B4-01`](../agent/cards/WI-B4-01-partition-counters.md) | Partition counters and hot-path audit | B4 |
| [`WI-B4-02`](../agent/cards/WI-B4-02-pair-once-partition-update.md) | Allocation-free, pair-once partition update | B4 |
| [`WI-B4-03`](../agent/cards/WI-B4-03-full-scale-verification.md) | Full-scale verification re-run | B4 |
| [`WI-B4-04`](../agent/cards/WI-B4-04-group-movement.md) | Group movement service | B4 |
| [`WI-B4-05`](../agent/cards/WI-B4-05-async-path-requests.md) | Asynchronous path requests | B4 |
| [`WI-B4-06`](../agent/cards/WI-B4-06-hierarchical-gate.md) | Hierarchical pathfinding gate | B4 |

One commit per card, one PR for the phase, left open for the operator. Cards land in order;
04..06 may interleave with the M4 preparation once 01..03 hold the partition budget.

Pending at authoring time: counter names as they land in the B1 record, measured medians
from the verification re-run, and the squad probe numbers. The hierarchical gate decision
landed in the WI-B4-06 section below.

## WI-B4-01 baseline (pre-fix, full scale)

Same profile and seed as the M1 record (`m1.capacity-probe`, seed 1, 9000 frames, deepdivide,
992 ground + 8 Aurora per side), run 2026-10-11 on the same Intel Core Ultra 7 265K host.
Wall 127.2 min (M1 recorded 119.8 min; same shape, different machine load).

Medians with 95% CI over 9000 measured ticks:

| Statistic | Median | CI |
|---|---|---|
| `tick.wallMs` | 941.9 | [792.0, 1087.7] |
| `system.partition.spanMs` | 783.9 | [719.6, 1058.4] |
| `partition.dirtyObjectsPerTick` | 1040 | [1040, 1041] |
| `partition.candidatePairsPerTick` | 850475 | [845458, 1317214] |
| `partition.exactHitsPerTick` | 714747 | [712590, 1079116] |

Decile medians of tick wall and partition span (armies close over deciles 0-3, contact from
decile 4): tick 57.7/136.6/228.3/219.2/747.6/1114.2/1149.8/1521.5/1575.6/1531.1 ms;
partition 21.2/88.2/159.4/161.7/701.6/1086.6/1125.2/1498.1/1551.9/1508.0 ms.

Reading: at steady contact about half the field (1040 objects) is dirty every tick, each dirty
object pairs against every cell-mate, and 84 percent of the ~850k candidate pairs pass the exact
test. Pair dedup and allocation removal (WI-B4-02) address the emission and allocation overhead;
the ~715k true overlaps per tick remain a workload fact that WI-B4-03 must report against the
10 ms budget honestly. Peak working set 1.1 GB versus the 7.5 GB recorded in the M1 run; recent
main changes (for example the X2-01 resource-lifetime work) are the likely cause and this card
does not investigate further.

Artifacts: `/tmp/opencode/b4-01-baseline/` (engagement report, B1 record, CSV, run log).

## WI-B4-03 verification (post-fix, full scale)

Same profile and seed, run 2026-10-12 on the same host, after the WI-B4-02 pair-once update.
Wall 48.3 min (baseline 127.2 min). Engagement outcome identical: both sides 992 + 8 spawned,
878/877 alive, 122/123 destroyed, 37 launches, 22 recoveries, `allPassed` true in both runs.

Medians with 95% CI over 9000 measured ticks, before and after:

| Statistic | Baseline median [CI] | After median [CI] |
|---|---|---|
| `tick.wallMs` | 941.9 [792.0, 1087.7] | 376.6 [369.5, 389.3] |
| `tick.fps` | 1.1 [0.9, 1.3] | 2.7 [2.6, 2.7] |
| `tick.allocBytes` | 97806448.0 [97.7M, 161.1M] | 27957532.0 [27.8M, 38.0M] |
| `system.partition.spanMs` | 783.9 [719.6, 1058.4] | 259.4 [248.4, 348.2] |
| `partition.dirtyObjectsPerTick` | 1040 [1040, 1041] | 1040 [1040, 1041] |
| `partition.candidatePairsPerTick` | 850475 [845458, 1317214] | 313258 [312207, 443725] |
| `partition.exactHitsPerTick` | 714747 [712590, 1079116] | 260918 [260508, 388571] |

Decile medians of the partition span (contact from decile 4): baseline
21.2/88.2/159.4/161.7/701.6/1086.6/1125.2/1498.1/1551.9/1508.0 ms; after
9.7/31.4/56.9/76.6/225.5/383.1/420.3/541.3/512.3/387.1 ms.

Budget verdict: missed. The 10 ms contact budget stands against a 259 ms median (225-541 ms
across contact deciles), a factor of roughly 26. The dedup removed 63 percent of candidate
pairs and exact tests (per-tick allocations fell 71 percent), but the residual is the genuine
workload: about 1040 dirty membership rebuilds and 261k true overlap tests per contact tick,
around one microsecond per pair including membership upkeep. Closing the remaining factor needs
a cheaper exact test (broad-phase rejection before `GeometryCollisionDetectionUtility`),
resting-contact throttling, or intra-tick parallelism (B3, already judged required by the M1
report). Per the card, this is recorded and stopped; further partition work is a new card for
the operator to schedule, not a quiet retry.

Artifacts: `/tmp/opencode/b4-03-after/` (engagement report, B1 record, CSV, run log).

## WI-B4-06 hierarchical gate

Anchor measurement on the same probe (deepdivide, 992 ground + 8 parked jets per side, seed 1),
frame budget 4500, async path bursts of 1000 requests every 300 frames (14 bursts, 14000
requests, requesters round-robin over alive ground units), `AsyncPathFrameBound` 30. The
anchor rides the real engagement: requests are real path computations over the live graph,
applied on the logic thread by the ordinary per-tick drain.

Results:

| Measurement | Value |
|---|---|
| Submitted / applied / cancelled | 14000 / 14000 / 0 |
| Deadline misses (waited more than 30 frames) | 208, all in the first burst (frame 300, worker warm-up); max wait 42 frames |
| Wait in frames (median / p90 / p99 / max) | 2 / 5 / 35 / 42 |
| Submit-to-apply wall (median / p99 / max) | 334.6 / 1660.3 / 2049.3 ms (dominated by one contact-phase tick) |
| Per-burst backlog | 984-1000 right after each burst, zero between bursts (no accumulation) |
| Steady-state drain | 1000 completions applied in 1-2 ticks per burst |

Drain overhead against the WI-B4-03 after-run, partition span medians by absolute frame
window: 10.3/29.1/53.5/74.5/225.7 ms versus 4.2/23.1/51.5/68.2/186.6 ms baseline. Early
windows carry the first-burst warm-up and burst allocations; the contact window stays within
21 percent of baseline while absorbing a 1000-request burst every 300 frames. Engagement
checks all passed (121/116 destroyed, `allPassed` true).

Verdict: the gate holds, no hierarchical layer. The flat queue meets the 30-frame soft
deadline for 13800 of 14000 requests (98.5 percent) at a burst rate far above any projected
consumer (M4 plans at most one request per unit per several seconds); the tail is first-burst
warm-up, not steady state. A hierarchical overlay would add cache and complexity for no
measured need. Revisit only if a future consumer sustains burst rates that push the median
wait past the bound.

Engine surface the anchor needed (not a hierarchical layer):
`Navigation.AsyncPathFrameBound` / `AsyncPathOutstanding` / `AsyncPathDeadlineMisses`,
`PathRequestQueue.Outstanding`, TestHarness knobs
`MassBattleCapacityProbeAsyncAnchorBurstEveryFrames` / `...RequestsPerBurst`, and the
`MassBattleAsyncPathAnchor` report riding the engagement report.

Artifacts: `/tmp/opencode/b4-06-anchor/` (engagement report with anchor section, B1 record,
CSV, run log).
