# Task Card `WI-W2-02`: Add transition records and schema additions

| Field | Value |
|---|---|
| Card ID | `WI-W2-02` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W2-01`; all WI-W1 cards; `WI-W0-02`; `WI-W0-07` |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Add reconstructable W2 transition records for promotion, demotion, and scheduler mode changes.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.

## Steps

1. Extend the Western Front state schema with `FidelityTransitionRecord` and active tactical area records.
2. Capture policy id, reason, input values, object ids, formation keys, and ledger moves.
3. Add domain-state artifact tests that reconstruct transitions without input re-simulation.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/State/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] Promotion and demotion records serialize and reconstruct.
- [ ] Records include moved personnel, MG, SAA, and artillery ammunition where applicable.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No policy decisions beyond consuming records from `WI-W2-01`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
