# Task Card `WI-W2-10`: Translate formation objectives to A4-3 orders

| Field | Value |
|---|---|
| Card ID | `WI-W2-10` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W2-05`; `WI-W2-08`; all WI-W1 cards; A4-3 hierarchy cards Done |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Translate W1 formation objectives into soldier orders through A4-3 hierarchy groupings.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.
- `src/Panopticon.Simulation/SimulationHost.cs:107-180`: injected order scheduling.
- `src/Panopticon.Simulation/Logic/AI/Hierarchy/HierarchicalAIDecisionPolicy.cs:39-68`: hierarchy to leaf orders.

## Steps

1. Register promoted soldiers into squad and section groupings.
2. Convert hold, move, attack, and withdraw objectives into commander directives or leaf goals.
3. Schedule resulting orders through `SimulationHost.InjectOrders`.
4. Add tests that no higher-order object mutates tactical state directly.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Command/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] Soldier orders enter through `SimulationHost.InjectOrders`.
- [ ] Squads and sections are hierarchy groupings, not `GameObject`s.
- [ ] Higher-level orders remain domain records.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No new neural policy work.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
