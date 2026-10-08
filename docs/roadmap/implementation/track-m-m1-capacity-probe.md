# Track M · M1: Capacity probe implementation plan

Source of truth: [M1 checklist items](../implementation-checklist.md#m1-capacity-probe),
[mass battle charter](../deep-dives/mass-battle-1k.md). Cards:
[`WI-M1-01..03`](../agent/cards/).

## What landed

| Card | Deliverable | Status |
|---|---|---|
| [`WI-M1-01`](../agent/cards/WI-M1-01-mass-battle-engagement-scenario.md) | `MassBattleCapacityProbe` built-in scenario: two hostile 1000-unit armies (992 ground + 8 parked `AmericaJetAurora` per side) on the M0 deepdivide map, one general attack, headless and windowed from one profile | Done |
| [`WI-M1-02`](../agent/cards/WI-M1-02-b1-capacity-measurements.md) | B1 measurement of the engagement: `BenchmarkMetricRecorder` samples, `Navigation.PathRequestCount`, `BenchmarkRunRecord` JSON/CSV beside the engagement report | Done |
| [`WI-M1-03`](../agent/cards/WI-M1-03-first-bottleneck-report.md) | This report: first failing constraint and the B2/B3 verdict | Done |

Engine fixes the scenario required (direct-spawned parked jets could not fly): lateral velocity
scrub for taxiing aircraft (`PhysicsBehavior.ScrubVelocityLateral2D`), takeoff speed from
`Physics.Velocity.Length()` instead of the never-populated `GameObject.Speed`, runway assignment
and taxiing-block fixes in `ParkingPlaceBehavior`, one-shot dying-slot cleanup, and
`AllowInvalidPosition` on taxiing locomotors.

## Run configuration

Profile `mass-battle-deepdivide-capacity-probe.json`: 600 `AmericaInfantryRanger` + 280
`AmericaTankPaladin` + 112 `AmericaVehicleTomahawk` + 8 `AmericaJetAurora` per side on
`maps/deepdivide/deepdivide.map`, airfields at the four profile anchors, hostile teams 0/1,
general attack at frame 30, frame budget 9000. Headless, fixed logic timestep, 30 logic frames
per second of sim time.

Environment: Intel Core Ultra 7 265K, 20 logical processors, 125 GB physical memory, GC
Workstation/Interactive, headless software path. Reproduction:

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
dotnet run --project src/Panopticon.Tools.TestHarness --no-build -- \
  --headless --built-in-scenario MassBattleCapacityProbe
```

A windowed run of the same scenario (no `--headless`) is the operator-run load proof; this host
cannot present a window from agent shells (see `docs/screenshots.md`), so the frame-rate record
below is the headless reciprocal tick time (`tick.fps = 1000 / tick.wallMs`), which equals the
windowed frame time whenever rendering is not the constraint.

## Headline medians

| Metric | Median | 95% CI | p10 | p90 | p99 |
|---|---|---|---|---|---|
| `tick.wallMs` | 1041.1 | [925.9, 1046.6] | 111.0 | 1509.7 | 1591.8 |
| `tick.fps` | 0.961 | [0.955, 1.080] | | | |
| `tick.allocBytes` | 97.8 MB | [97.7, 161.1] | | | |
| `system.partition.spanMs` | 666.0 | [652.7, 1013.5] | | | |
| `system.gameLogic.spanMs` | 18.853 | [18.787, 18.942] | | | |
| `system.scene3d.spanMs` | 2.347 | [2.320, 2.394] | | | |
| `system.network.spanMs` | 0.003 | | | | |
| `path.requestsPerTick` | 0.000 | | | | |

Engagement outcome: 878/877 units alive at the budget, 122/123 destroyed, 37 jet launches and 22
recoveries across 16 jets (13 flew at least two sorties; rearm observed at full scale). Wall
time 2 h 04 min for 9000 frames (5 sim-minutes).

## Cost over the engagement

Medians per 900-frame decile. Armies close during deciles 0-3; sustained contact from decile 4.
Memory columns are cumulative or per-tick as labeled.

| Decile | tick.wallMs | partition | gameLogic | scene3d | alloc/tick | heap (cum.) | working set (cum.) |
|---|---|---|---|---|---|---|---|
| 0 | 56.0 | 21.1 | 29.9 | 4.2 | 10.6 MB | 0.34 GB | 0.65 GB |
| 1 | 130.8 | 86.4 | 27.0 | 8.4 | 15.3 MB | | |
| 2 | 226.0 | 156.7 | 23.8 | 27.6 | 23.5 MB | | |
| 3 | 228.1 | 157.6 | 21.3 | 8.4 | 27.0 MB | | |
| 4 | 661.0 | 599.2 | 18.8 | 10.3 | 89.8 MB | | |
| 5 | 1046.9 | 1022.6 | 18.4 | 2.2 | 153.9 MB | | |
| 6 | 1078.8 | 1056.9 | 18.3 | 2.2 | 155.0 MB | | |
| 7 | 1447.9 | 1427.1 | 17.4 | 2.1 | 185.3 MB | | |
| 8 | 1507.6 | 1486.5 | 17.2 | 2.2 | 190.7 MB | 1.36 GB | 7.47 GB |
| 9 | 1226.9 | 1205.8 | 17.6 | 2.1 | 163.8 MB | | |

GC to end of run: 34.2 M gen0, 5.84 M gen1, 613.8 k gen2 collections; the final decile alone
accounts for roughly 3.4 M gen0, 580 k gen1, and 61 k gen2. The unattributed remainder (tick
minus the four system spans) has median 1.4 ms, so the span instrumentation accounts for
essentially the whole tick.

## First failing constraint

The partition system span is the first constraint that fails. It crosses the 33.3 ms (30 fps)
budget around frames 900-2700, while the armies are still closing and before any sustained
contact; by decile 4 it is 599 ms and through contact it carries 86-99 percent of every tick.
Its cost grows as the armies concentrate into the same cells, consistent with per-cell pairwise
work; no fixed-density sweep was run, so no scaling exponent is claimed here. For scale:
gameLogic medians 17-30 ms and scene3d medians 2-28 ms (its one spike, 27.6 ms in decile 2,
coincides with the all-units-move approach; it settles near 2 ms once armies engage).

Second constraint: allocation churn and GC. At contact the run allocates 98-190 MB per tick
(gen0 roughly 380 collections per tick in the final decile) and the working set reaches 7.5 GB
at 2000 units, about 3.7 MB per unit. Churn inflates every span, including partition's.

Not a constraint at this composition: pathfinding. The probe's scripted orders issue moves once
per unit (plus a refresh every 300 frames for units that arrived), so `path.requestsPerTick`
medians 0 and the load is front-loaded into the closing phase. Path cost also sits inside the
`gameLogic` span, which stays flat at 17-30 ms throughout.

Extrapolation, not measurement: doubling to 4000 units without changing local density would
roughly double the per-tick work; concentration-driven growth means the 2000-unit medians above
are lower bounds for a same-map 4000-unit battle.

## B2/B3 verdict for M5

| Phase | Verdict | Reason |
|---|---|---|
| B2 (allocation and hot-path cleanup) | Required | 98-190 MB/tick churn inflates every span and drives the GC load; cheapest fix and the prerequisite for measuring anything else cleanly. |
| B3 (parallel and data-oriented logic) | Required | even at zero allocation, the serial partition plus gameLogic cost is about 700 ms at contact against a 33 ms budget; parallelism is the only route to that factor. |
| B4 (spatial and pathfinding scaling) | Already an M5 dependency | B3 parallelizes work but does not change partition's growth with local density; the algorithmic partitioning fix is B4's scope. |

M5 gap at the current engine: median `tick.fps` 0.96 against the 30 fps live-view target (about
31x), and a 5-sim-minute engagement costs about 2 hours of wall time headless.

## Artifacts

- `/tmp/opencode/m1-full-run/mass-battle-capacity-probe-report.json` (engagement: sides, sorties)
- `/tmp/opencode/m1-full-run/mass-battle-capacity-probe-report-benchmark.json` (B1 record, 4.9 MB)
- `/tmp/opencode/m1-full-run/mass-battle-capacity-probe-report-benchmark.csv` (derived rows)

Fresh runs write the same names beside `src/Panopticon.Tools.TestHarness/bin/Debug/net10.0/` or
wherever `MassBattleCapacityProbeReportPath` points.

## Follow-ups recorded

- Per-tick AI decision cost counter does not exist yet; M4 needs one (this probe runs with AI
  disabled, so its numbers exclude AI cost by construction).
- A windowed frame-time record needs a presenting display; the headless `tick.fps` reciprocal is
  the interim frame-rate measurement.
- The partition span is the measured partition load; a finer per-subsystem breakdown belongs to
  B4 when it instruments partition internals.
