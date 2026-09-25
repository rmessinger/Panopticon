# Track W · W0: Charter fixture

> Audience: human overview above the divider; agent-context detail below it.

Implements the W0 row of [Track W](../track-w-western-front.md). Design source of truth:
[deep-dives/w0-charter-fixture.md](../deep-dives/w0-charter-fixture.md), including its
[architecture diagrams](../deep-dives/w0-charter-fixture.md#12-architecture-diagrams).

## Scope

Build the Western Front domain skeleton in `Panopticon.Simulation.WesternFront`: stable keys down
to platoon depth, one-soldier tactical records, the A4-4 state schema, a synthetic topology and
scenario, the multi-rate theater clock, the domain adapter, and the conservation ledger. W0
measures only; it adds no gameplay systems.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| W0-1 | [`WI-W0-01`](../agent/cards/WI-W0-01-scaffold-western-front-project.md) | Project, `InternalsVisibleTo`, keys, enums. |
| W0-2 | [`WI-W0-02`](../agent/cards/WI-W0-02-western-front-state-schema.md) | `WesternFrontSchema` v1. |
| W0-3 | [`WI-W0-03`](../agent/cards/WI-W0-03-synthetic-topology.md) | Sectors, regions, 410-unit cells, nodes, links. |
| W0-4 | [`WI-W0-04`](../agent/cards/WI-W0-04-theater-clock-scheduler.md) | `TheaterClock`, `WesternFrontScheduler`, 1 min and 1 h cadence. |
| W0-5 | [`WI-W0-05`](../agent/cards/WI-W0-05-formation-registry-modules.md) | State modules, `FormationRegistry`, soldier records. |
| W0-6 | [`WI-W0-06`](../agent/cards/WI-W0-06-synthetic-scenario.md) | `WesternFrontScenario.CreateSynthetic`. |
| W0-7 | [`WI-W0-07`](../agent/cards/WI-W0-07-western-front-domain-adapter.md) | `WesternFrontDomainModel`. |
| W0-8 | [`WI-W0-08`](../agent/cards/WI-W0-08-conservation-ledger.md) | `WesternFrontLedger`, residual metrics. |
| W0-9 | [`WI-W0-09`](../agent/cards/WI-W0-09-charter-fixture-evidence.md) | End-to-end evidence test; closes W0. |

---

## Agent context

## Dependency order

```text
WI-W0-01 ─┬─ WI-W0-02 (also WI-A4-42) ─ WI-W0-05 ─┐
          ├─ WI-W0-03 ────────────────────────────┴─ WI-W0-06 ─┐
          └─ WI-W0-04 ─────────────────────────────────────────┴─ WI-W0-07 (also WI-A4-44) ─ WI-W0-08 ─ WI-W0-09
```

`WI-W0-03` and `WI-W0-04` can run in parallel with the A4-4 cards.

## Layout

| Folder | Contents |
|---|---|
| `src/Panopticon.Simulation.WesternFront/` | keys, enums, `WesternFrontDomainModel.cs` |
| `.../Clock/` | `TheaterClock`, options, scheduler, `IWesternFrontSystem` |
| `.../State/` | schema, state classes, ledger |
| `.../Runtime/` | behavior modules, `FormationRegistry` |
| `.../Topology/` | topology types |
| `.../Scenario/` | scenario and options |
| `test/Panopticon.Simulation.Tests/WesternFront/` | all W0 tests |

## Invariants

- Echelon enum values are `Army = 0`, `Corps = 1`, `Division = 2`, `Brigade = 3`,
  `Battalion = 4`, `Company = 5`, `Platoon = 6`.
- No W0 module overrides a per-frame update; the scheduler is the only driver.
- Compressed mode advances 60 theater seconds per host frame; campaign systems run every 60
  compressed frames.
- Tactical records are soldiers. Squads are A4-3 grouping state only.
- Conserved fields change only through `WesternFrontLedger` (from `WI-W0-08` on).
- No change to `SimulationHost` or other RTS code paths beyond the `InternalsVisibleTo` line.

Code hooks: see the deep dive's
[appendix](../deep-dives/w0-charter-fixture.md#appendix-current-state-hooks-agent-context).
