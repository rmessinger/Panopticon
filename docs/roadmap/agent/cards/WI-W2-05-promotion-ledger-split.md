# Task Card `WI-W2-05`: Split platoon holdings onto soldiers

| Field | Value |
|---|---|
| Card ID | `WI-W2-05` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W2-02`; `WI-W2-03`; `WI-W2-04`; all WI-W1 cards; `WI-W0-08` |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Implement promotion ledger moves from platoons to soldier records, including largest-remainder SAA.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.

## Steps

1. Add promotion allocation records per platoon.
2. Move one personnel to each created soldier.
3. Preserve MG count by assigning MG carriers deterministically.
4. Distribute SAA with largest remainder and record every move.
5. Add conservation residual tests.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Promotion/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] Personnel split is exact and integer.
- [ ] MG count is preserved.
- [ ] SAA largest-remainder split leaves no unallocated rounds.
- [ ] Conservation residuals are empty after promotion.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No tactical combat logic.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
