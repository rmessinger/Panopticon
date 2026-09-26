# Task Card `WI-W2-08`: Preserve MG teams through promotion and demotion

| Field | Value |
|---|---|
| Card ID | `WI-W2-08` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W2-05`; `WI-W2-07`; all WI-W1 cards |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Assign MG carriers during promotion and reconcile MG equipment through deaths and demotion.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.

## Steps

1. Add deterministic MG carrier selection from platoon holdings.
2. Apply the planned MG weapon-set marker or module state.
3. Record MG carrier object ids in transition records.
4. On death or demotion, reconcile surviving and lost MG counts.
5. Add tests for carrier assignment and MG loss.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Tactical/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] MG carriers equal the platoon MG allocation.
- [ ] Dead MG carriers move MG to the lost sink or documented recovery path.
- [ ] Surviving MG counts return to the platoon on demotion.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No new MG art content.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
