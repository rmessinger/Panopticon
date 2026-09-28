# Task Card `WI-W3a-15`: Add weapon hit resolution

| Field | Value |
|---|---|
| Card ID | `WI-W3a-15` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-14`, `WI-W1-03` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-14` and `WI-W1-03` are Done and the seeded stream types are reconciled.

Introduce a null-by-default weapon hit-resolution seam and the period resolver behind it.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §7.1 and §13.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §8.2.
- `src/Panopticon.Simulation/Logic/Object/Weapon/Weapon.cs:157` and `WeaponTarget.cs:36-50`: fire seam and damage call.
- `src/Panopticon.Mathematics/IRandom.cs:6-33` and `src/Panopticon.Mathematics/IRandom.cs:74-135`: seeded draws.

## Steps

1. Add `IWeaponHitResolver` and wire it into the `Weapon` fire path before `WeaponTarget.DoDamage`.
2. Keep the resolver null by default and preserve stock behavior.
3. Implement `PeriodHitResolver` using range band, posture, cover, visibility multiplier, and fire mode.
4. Draw one value per shot on stream `runSeed|cellKey|frameIndex|fire` in ascending shooter object id order.
5. Add tests for null resolver identity and period hit-table outcomes.

## Files likely to change

- `src/Panopticon.Simulation/Logic/Object/Weapon/Weapon.cs`
- Western Front period hit resolver files
- Weapon and Western Front tests

## Acceptance criteria

- [ ] Null resolver keeps stock Generals weapon behavior byte-identical.
- [ ] A miss spends the round and applies no damage.
- [ ] Period resolver consumes draws in ascending shooter object id order.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WeaponHitResolver"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No global change to Generals direct-fire semantics.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
