# Task Card `WI-W2-12`: Switch modes with the W0 scheduler

| Field | Value |
|---|---|
| Card ID | `WI-W2-12` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W2-01`; `WI-W2-11`; all WI-W1 cards; `WI-W0-04` |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Integrate W2 activation with the W0 scheduler so promotion and demotion change mode only on operational boundaries.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.

## Steps

1. Queue tactical mode requests from activation results.
2. Apply promotions and demotions only on operational boundaries.
3. Enter tactical mode while any active tactical region exists.
4. Return to compressed mode after all active regions demote.
5. Add clock conversion and boundary tests.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Clock/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] Mode changes happen only on operational boundaries.
- [ ] Tactical mode stays active while any tactical region exists.
- [ ] Clock conversion records include W2 transition ids.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No new scheduler cadence values beyond the W2 defaults.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
