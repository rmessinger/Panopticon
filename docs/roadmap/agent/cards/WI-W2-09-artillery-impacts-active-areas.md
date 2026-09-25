# Task Card `WI-W2-09`: Apply strategic artillery impacts in active areas

| Field | Value |
|---|---|
| Card ID | `WI-W2-09` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W2-02`; `WI-W2-06`; all WI-W1 cards |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Route operational bombard orders into active tactical areas as seeded damage impacts.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.
- `src/Panopticon.Simulation/Logic/Object/Damage/DamageInfo.cs:38-130`: damage input shape.
- `src/Panopticon.Simulation/Logic/Object/GameObject.cs:272-279`: object damage entry point.

## Steps

1. Add an artillery impact system for bombard orders targeting active tactical areas.
2. Draw impact points from the order target area using W1 seeded stream rules.
3. Consume operational artillery rounds through the ledger.
4. Apply `DamageInfoInput` to soldiers inside the impact area.
5. Record impact points and consumed rounds.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Tactical/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] Bombard orders consume artillery rounds once.
- [ ] Impact points are deterministic per seed.
- [ ] Damage is applied through `GameObject.AttemptDamage`.
- [ ] Impact records reconstruct from the artifact.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not create tactical artillery `GameObject`s in W2.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
