# Task Card `WI-W2-13`: Create the whole-sector all-tactical reference run

| Field | Value |
|---|---|
| Card ID | `WI-W2-13` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W2-12`; all WI-W1 cards |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Add the W2 reference scenario that materializes the whole sector tactically from frame zero.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.

## Steps

1. Add `AllTacticalActivationPolicy` scenario wiring.
2. Materialize all sector platoons into soldier objects at run start.
3. Mark the run artifact as reference and primary product.
4. Add an evidence test for full materialization and recording.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Scenario/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] Whole sector is tactical from frame zero.
- [ ] Reference artifact records all promoted soldiers and transition records.
- [ ] The scenario can run at least one shortened evidence interval.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not optimize for real-time execution.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
