# Task Card `WI-W2-07`: Add per-soldier SAA consumption

| Field | Value |
|---|---|
| Card ID | `WI-W2-07` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W2-05`; all WI-W1 cards |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Add a per-soldier ammo module that decrements SAA on shots and blocks firing at zero.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.
- `src/Panopticon.Simulation/Logic/Object/Weapon/Weapon.cs:29-35`: weapon clip rounds.
- `src/Panopticon.Simulation/Logic/Object/Weapon/WeaponStates/FiringWeaponState.cs:21-29`: clip decrement on firing.

## Steps

1. Add `SoldierAmmoModule` state with assigned SAA rounds.
2. Hook the firing path or a narrow W2 weapon gate so each shot consumes one SAA unit.
3. Block firing when assigned SAA is zero.
4. Sink consumed SAA through the ledger.
5. Add tests for decrement, zero block, and demotion return of unused rounds.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Tactical/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] Firing decrements soldier SAA.
- [ ] Zero SAA prevents additional shots.
- [ ] Consumed and unused SAA reconcile exactly.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not rewrite the general weapon state machine beyond the minimal hook.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
