# Task Card `WI-W2-04`: Place soldiers from posture and terrain

| Field | Value |
|---|---|
| Card ID | `WI-W2-04` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W2-03`; all WI-W1 cards; `WI-W0-03` |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Add deterministic soldier placement for promoted platoons based on posture, trench level, and active cells.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.

## Steps

1. Add posture-to-placement-band rules.
2. Use region trench level to select front, support, reserve, shell-hole, or open bands.
3. Draw intra-band offsets from the seeded stream.
4. Add tests for determinism and containment in active cells.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Tactical/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] Same seed produces identical positions.
- [ ] Different posture values select the documented bands.
- [ ] Every soldier position remains inside the active tactical area.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No pathfinding or terrain deformation.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
