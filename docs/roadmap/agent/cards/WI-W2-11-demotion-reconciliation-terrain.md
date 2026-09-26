# Task Card `WI-W2-11`: Demote soldiers and fold terrain summaries back

| Field | Value |
|---|---|
| Card ID | `WI-W2-11` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W2-06`; `WI-W2-07`; `WI-W2-08`; `WI-W2-09`; all WI-W1 cards |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Implement demotion that reconciles soldiers, ammo, MG, casualties, ownership, and terrain summaries.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.

## Steps

1. Capture final live soldier state before object destruction.
2. Move surviving personnel, MG, and SAA back to platoons.
3. Keep casualty and consumption sinks unchanged.
4. Fold crater, wire, and trench changes into region records.
5. Destroy tactical soldier objects and record reconciliation.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Promotion/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] Demotion leaves empty conservation residuals.
- [ ] Terrain summaries are updated from tactical effects.
- [ ] Soldier object ids are removed from active mappings.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No save/load work beyond recorded state.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
