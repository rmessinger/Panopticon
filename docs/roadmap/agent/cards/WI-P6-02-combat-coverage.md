# Task Card: `WI-P6-02` — Combat coverage

| Field | Value |
|---|---|
| Card ID | `WI-P6-02` |
| Roadmap phase | [Track P · P6](../../track-p-playable-baseline.md) — sub-phase P6-2, see [deep dive §P6](../../deep-dives/economy-production-combat.md#p6--combat--command-verification) and [§P6 sub-phases](../../deep-dives/economy-production-combat.md#p6-sub-phases) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | P1 spawned/related players and owner tracking; `WI-P6-01` useful but not required |
| Diagrams | [P6 Architecture Diagrams](../../deep-dives/economy-production-combat.md#architecture-diagrams) |

## Goal

Verify and close the baseline combat path: weapon fire applies damage through `DamageModule`/`ActiveBody`, target health reaches zero, and death is triggered for baseline units and damage types.

## Context to read (and only this)

- `docs/roadmap/deep-dives/economy-production-combat.md:76-95` — P6 verify-and-close design and combat sub-phase.
- `docs/roadmap/implementation-checklist.md:283-286` — checklist ACs for weapon/damage/death coverage.
- `src/OpenSage.Game/Logic/Object/Weapon/Weapon.cs:101-140` — target assignment and weapon firing.
- `src/OpenSage.Game/Logic/Object/Weapon/WeaponTarget.cs:36-46` — target damage dispatch.
- `src/OpenSage.Game/Logic/Object/Weapon/WeaponEffects/DamageNugget.cs:115-126` — weapon nugget damage execution.
- `src/OpenSage.Game/Logic/Object/Damage/DamageModule.cs:9-18` and `:51-75` — damage callbacks and damage-type helpers.
- `src/OpenSage.Game/Logic/Object/Body/ActiveBody.cs:58-83` and `:302-361` — health reduction, damage callbacks, and death trigger.
- `src/OpenSage.Game/Logic/Object/Update/FireWeaponUpdate.cs:29-33` and `:85-123` — current fire-weapon update behavior and parsed data.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:1284-1337` — `Kill`/`OnDie` path and selection side effects.
- `src/OpenSage.Game.Tests/Logic/Object/Body/ActiveBodyTests.cs:6-28` — existing active-body test style.
- `src/OpenSage.Game.Tests/Logic/Object/ModuleTest.cs:5-12` — module test base.

## Steps

1. Add `src/OpenSage.Game.Tests/Logic/Object/CombatCoverageTests.cs` as a headless `[Fact]` test file. Use `MockedGameTest`/`ModuleTest`; do not require game files.
2. Build a small baseline table of faction/reference attacker, target, weapon damage type, and expected kill outcome. Keep it small enough for S scope: at least one infantry/vehicle/building-relevant baseline case, and list any missing baseline damage type as an explicit gap.
3. Exercise the real path where possible: assign a weapon target, execute a `DamageNugget` or equivalent real weapon damage path, and assert `ActiveBody.Health` decreases through `AttemptDamage`.
4. Assert lethal damage drives health to zero or below and invokes `GameObject.OnDie`/death state. Verify the default/sentinel damage values used in the test are named and asserted.
5. If `FireWeaponUpdate.Update()` cannot yet fire its parsed weapon data, keep that as an explicit failing gap or documented assertion; do not silently pass with a no-op.
6. Preserve every branch/guard if moving any damage or weapon logic.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then verify it is clean.

## Files likely to change

- `src/OpenSage.Game.Tests/Logic/Object/CombatCoverageTests.cs` (new)
- `src/OpenSage.Game/Logic/Object/Weapon/Weapon.cs` only if a tiny missing seam is required to test the real path
- `src/OpenSage.Game/Logic/Object/Weapon/WeaponTarget.cs` only if a tiny missing seam is required to test the real path
- `src/OpenSage.Game/Logic/Object/Weapon/WeaponEffects/DamageNugget.cs` only if a tiny missing seam is required to test the real path
- `src/OpenSage.Game/Logic/Object/Body/ActiveBody.cs` only if a tightly coupled bug blocks the verified path
- `src/OpenSage.Game/Logic/Object/Update/FireWeaponUpdate.cs` only if closing its no-op behavior is required for the asserted path

## Acceptance criteria

- [ ] For baseline units, weapon fire reduces target health via `DamageModule`/`ActiveBody`.
- [ ] Lethal weapon damage triggers death at health `0` and the test asserts a kill.
- [ ] Damage-type defaults/sentinels in the tests are named and verified.
- [ ] `FireWeaponUpdate` participation is either verified through the real path or listed as a loud explicit gap.
- [ ] Tests run in CI as non-skipped `[Fact]` tests; no gate is satisfied only by `[GameFact]` or an Interactive-only test.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~CombatCoverageTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement the full P6b unit-contract matrix here.
- Do not add broad combat refactors; only close gaps directly required to verify weapon→damage→health→death.
- Do not remove or weaken existing damage guards, armor handling, or death behavior branches.
- Do not rely on skipped game-install tests for acceptance.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P6 weapon/damage/death coverage item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` listing verified baseline cases and explicit gaps.
- Confirm `src/OpenSage.Game.Tests/Logic/Object/CombatCoverageTests.cs` was created and runs in CI.
- Do not mark Track P P6 `Done` unless all P6 cards are complete.

## Result

<!-- append after completing -->
