# Track W · W1: Aggregate sector simulation

> Audience: human overview above the divider; agent-context detail below it.

Implements the W1 row of [Track W](../track-w-western-front.md). Design source of truth:
[deep-dives/w1-aggregate-sector.md](../deep-dives/w1-aggregate-sector.md), including its
[architecture diagrams](../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams).

## Scope

Extend the W0 fixture into a compressed, aggregate-only seven-day sector run. W1 adds unit-checked
parameters, schema v2 command and logistics records, deterministic streams, delayed
communications, echelon-local intelligence, reactive rule policies, operational movement,
logistics, consumption, platoon-level aggregate combat, campaign systems, evidence tests, and a
64-seed A2 report.

## Sub-phase to card mapping

| Sub-phase | Cards | Scope |
|---|---|---|
| W1-1 | [`WI-W1-01`](../agent/cards/WI-W1-01-parameters-loader.md), [`WI-W1-02`](../agent/cards/WI-W1-02-schema-v2.md), [`WI-W1-03`](../agent/cards/WI-W1-03-seeded-streams-poisson.md) | Parameters with unit schema, schema v2, XxHash64 streams, Poisson. |
| W1-2 | [`WI-W1-04`](../agent/cards/WI-W1-04-message-queue-commlinks.md) | Message queue, comm links, communication system. |
| W1-3 | [`WI-W1-05`](../agent/cards/WI-W1-05-intelligence-echelon-view.md), [`WI-W1-06`](../agent/cards/WI-W1-06-command-policies.md) | Intelligence pictures, `EchelonView`, reactive policies. |
| W1-4 | [`WI-W1-07`](../agent/cards/WI-W1-07-movement-system.md) | Movement and stacking. |
| W1-5 | [`WI-W1-08`](../agent/cards/WI-W1-08-logistics-allocation.md), [`WI-W1-09`](../agent/cards/WI-W1-09-shipments-interdiction-consumption.md) | Allocation, shipments, interdiction, consumption. |
| W1-6 | [`WI-W1-10`](../agent/cards/WI-W1-10-aggregate-combat-stochastic.md), [`WI-W1-11`](../agent/cards/WI-W1-11-aggregate-combat-mean-field.md), [`WI-W1-12`](../agent/cards/WI-W1-12-morale-terrain-outcomes.md), [`WI-W1-13`](../agent/cards/WI-W1-13-campaign-systems.md) | Combat variants, morale, terrain, outcomes, campaign systems. |
| W1-7 | [`WI-W1-14`](../agent/cards/WI-W1-14-scenario-evidence-tests.md), [`WI-W1-15`](../agent/cards/WI-W1-15-a2-experiment-report.md) | Seven-day scenario, evidence tests, 64-seed report. |

---

## Agent context

## Dependency table

| Card | Depends on | Unblocks |
|---|---|---|
| WI-W1-01 | WI-W0-09 | WI-W1-02, WI-W1-03, WI-W1-13 |
| WI-W1-02 | WI-W1-01, WI-W0-09 | WI-W1-04, WI-W1-08 |
| WI-W1-03 | WI-W1-01 | WI-W1-04, WI-W1-09, WI-W1-10 |
| WI-W1-04 | WI-W1-02, WI-W1-03 | WI-W1-05, WI-W1-14 |
| WI-W1-05 | WI-W1-04 | WI-W1-06 |
| WI-W1-06 | WI-W1-05 | WI-W1-07, WI-W1-14 |
| WI-W1-07 | WI-W1-06, WI-W0-03 | WI-W1-14 |
| WI-W1-08 | WI-W1-02, WI-W0-08 | WI-W1-09 |
| WI-W1-09 | WI-W1-08, WI-W1-03 | WI-W1-10, WI-W1-14 |
| WI-W1-10 | WI-W1-03, WI-W1-09 | WI-W1-11, WI-W1-12 |
| WI-W1-11 | WI-W1-10 | WI-W1-12 |
| WI-W1-12 | WI-W1-10, WI-W1-11 | WI-W1-14 |
| WI-W1-13 | WI-W1-01, WI-W0-08 | WI-W1-14 |
| WI-W1-14 | WI-W1-04, WI-W1-06, WI-W1-07, WI-W1-09, WI-W1-12, WI-W1-13 | WI-W1-15 |
| WI-W1-15 | WI-W1-14 | W1 milestone |

## Layout

| Folder | Contents |
|---|---|
| `src/Panopticon.Simulation.WesternFront/Parameters/` | unit-checked parameter schema and loader |
| `.../Random/` | XxHash64 stream derivation, `SageRandom` factory, Poisson sampler |
| `.../Command/` | orders, reports, message queue, policies |
| `.../Intelligence/` | contacts, `IntelligencePicture`, `EchelonView` |
| `.../Movement/` | route progress and stacking helpers |
| `.../Logistics/` | demand, largest-remainder allocation, shipments |
| `.../Combat/` | stochastic and mean-field aggregate combat |
| `.../Systems/` | W1 `IWesternFrontSystem` implementations |
| `.../Experiments/` | A2-compatible 64-seed report helpers |
| `test/Panopticon.Simulation.Tests/WesternFront/` | targeted tests for every W1 card |

## Invariants

- W1 stays compressed-only: one host frame equals one 60 s operational tick.
- Campaign systems run every 60 operational ticks, one theater hour.
- Combat resolves at platoon level.
- Random draws derive from XxHash64 tuple streams, not shared iteration-order state.
- Mean-field state uses fixed-point `long` at 1e-6 personnel.
- Every conserved mutation uses `WesternFrontLedger`.
- Policies read only `EchelonView`; no policy reads omniscient scenario state.
- Parameters are unit checked. Bare numeric JSON parameters are invalid.
- W1 cards are Draft until their prerequisite W0 and earlier W1 types exist and names are reconciled.

Code hooks: see the deep dive's
[appendix](../deep-dives/w1-aggregate-sector.md#appendix-current-state-hooks-agent-context).
