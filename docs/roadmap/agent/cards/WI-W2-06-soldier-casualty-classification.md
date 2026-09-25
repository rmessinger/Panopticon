# Task Card `WI-W2-06`: Classify soldier deaths into casualty sinks

| Field | Value |
|---|---|
| Card ID | `WI-W2-06` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W2-05`; all WI-W1 cards |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Add a soldier death classifier that sinks killed, wounded, or missing counts on the seeded stream.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.
- `src/Panopticon.Simulation/IGame.cs:149-153`: object destruction event.
- `src/Panopticon.Simulation/Logic/Object/Body/ActiveBody.cs:356-362`: death on health reaching zero.

## Steps

1. Add `SoldierCasualtyClassifier` and parameter fractions.
2. Subscribe to the appropriate object death or lifecycle event for promoted soldiers.
3. Draw killed, wounded, or missing from the seeded stream.
4. Sink exactly one personnel casualty per soldier death.
5. Add deterministic classification tests.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Tactical/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] Every promoted soldier death produces one casualty sink.
- [ ] Fractions are read from W1 parameters.
- [ ] Same seed and death order produce identical classifications.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No wounded-state `GameObject` remains in tactical W2.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
