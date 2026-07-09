# Task Card: `WI-P4-03` — Power loop verification

| Field | Value |
|---|---|
| Card ID | `WI-P4-03` |
| Roadmap phase | [Track P · P4](../../track-p-playable-baseline.md) — sub-phase P4-3, see [P4 deep dive](../../deep-dives/p4-p6-economy-production-combat.md#p4-economy-loop-end-to-end) and [P4 sub-phases](../../deep-dives/p4-p6-economy-production-combat.md#p4-sub-phases) |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | P1 complete enough that player ownership and owned-object energy scans are reliable |
| Diagrams | [P4 Architecture Diagrams](../../deep-dives/p4-p6-economy-production-combat.md#architecture-diagrams) |

## Goal

Verify and close the power loop: owned objects' energy production/consumption drive `Player._hasInsufficientPower`, and the confirmed low-power effect is asserted by a headless test. The deep dive marks the exact effect as TBD, so first verify the intended effect from existing code/data and only implement the smallest missing wiring.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:16-55` — P4 power-loop design and the warning that the intended low-power effect must be verified.
- `docs/roadmap/implementation-checklist.md:244-247` — checklist AC template for `_hasInsufficientPower` and its defined effect.
- `src/OpenSage.Game/Logic/Player.cs:41-104` — `_hasInsufficientPower` field and `GetEnergy(...)` summing owned `EnergyProduction`.
- `src/OpenSage.Game/Logic/Player.cs:550-554` — `_hasInsufficientPower` persistence; preserve field order.
- `src/OpenSage.Game/Logic/Object/ObjectDefinition.cs:100-111` — INI parse entries for `BuildCost`, `EnergyProduction`, and `EnergyBonus`.
- `src/OpenSage.Game/Logic/Object/ObjectDefinition.cs:512-517` — stored energy fields.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:390` and `src/OpenSage.Game/Logic/Object/GameObject.cs:1060-1068` — runtime energy field and construction completion adding energy production.
- `src/OpenSage.Game/Logic/Object/Production/ProductionJob.cs:10-12` — existing low-energy production-speed TODO; use as evidence, not as a design mandate.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-34` — headless test base; tests here run in CI.

## Steps

1. Investigate the existing data model for consumers. Confirm whether consumption is represented by negative `EnergyProduction`, another field, or missing data. Do not invent a new power model without evidence.
2. Find where per-player power should be evaluated each tick. Prefer an existing player/game logic tick path over adding a new subsystem.
3. If `_hasInsufficientPower` is not currently updated, add minimal deterministic wiring: compare owned production against owned consumption/negative production, set the field, and keep the default false when no owned power consumers exist.
4. Verify the intended effect of insufficient power from existing code/data. If the only confirmed effect is a currently missing production slowdown/disabled-structure behavior, implement the smallest version and document it in the test name/assertions.
5. Preserve persistence ordering in `Player.Persist(...)` and all existing construction/energy guards.
6. Add `src/OpenSage.Game.Tests/Logic/Economy/PowerLoopTests.cs` with non-skipped headless `[Fact]` tests that:
   - create enough owned power production for a sufficient-power state;
   - add/toggle consumption so consumption exceeds production;
   - assert `_hasInsufficientPower` (or a test-visible API exposing it) changes deterministically;
   - assert the verified effect occurs and then clears when power is restored.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/Player.cs`
- `src/OpenSage.Game/Logic/Object/Production/ProductionJob.cs` only if the verified effect is production speed
- `src/OpenSage.Game/Logic/Object/Update/ProductionUpdate.cs` only if the verified effect is production gating/slowdown
- `src/OpenSage.Game.Tests/Logic/Economy/PowerLoopTests.cs` (new)

## Acceptance criteria

- [ ] The card result documents the verified representation of power consumption and the verified low-power effect.
- [ ] `_hasInsufficientPower` is false by default and becomes true when owned consumption exceeds owned production.
- [ ] Restoring enough power clears `_hasInsufficientPower`.
- [ ] A non-skipped headless `[Fact]` toggles power and asserts the defined effect; no skipped `[GameFact]` is used as the gate.
- [ ] Any default/sentinel values are named or asserted: no consumers, exact balance point, insufficient by one unit, restored power.
- [ ] Existing `Player.Persist(...)` field order and every existing construction/energy branch are preserved.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~PowerLoopTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not invent a new SAGE power design; the deep dive explicitly leaves the exact effect as "AC: TBD — verify intended effect."
- Do not implement UI warnings, audio, or visual power effects in this card.
- Do not change build costs, build times, or object definitions beyond what is necessary for a mocked headless test.
- If the intended effect cannot be verified from code/data, mark `Blocked` with findings instead of guessing.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the power model interactions P4 item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` naming the verified low-power effect.
- Confirm `src/OpenSage.Game.Tests/Logic/Economy/PowerLoopTests.cs` was created and runs in CI.

## Result

<!-- append after completing -->
