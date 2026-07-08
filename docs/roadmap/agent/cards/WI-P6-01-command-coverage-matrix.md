# Task Card: `WI-P6-01` — Command coverage matrix

| Field | Value |
|---|---|
| Card ID | `WI-P6-01` |
| Roadmap phase | [Track P · P6](../../track-p-playable-baseline.md) — sub-phase P6-1, see [deep dive §P6](../../deep-dives/economy-production-combat.md#p6-combat-and-command-verification) and [§P6 sub-phases](../../deep-dives/economy-production-combat.md#p6-sub-phases) |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | P1 spawned/related players and owner tracking; P0 harness command/assertion path available |
| Diagrams | [P6 Architecture Diagrams](../../deep-dives/economy-production-combat.md#architecture-diagrams) |

## Goal

Create a CI-running command coverage matrix proving every baseline control-bar action maps to an `OrderType` with a real `OrderProcessor` handler, and that a harness command for each action produces the expected observable effect or an explicit gap.

## Context to read (and only this)

- `docs/roadmap/deep-dives/economy-production-combat.md:56-95` — P6 goal, design, and P6-1 sub-phase.
- `docs/roadmap/deep-dives/economy-production-combat.md:132-144` — P6 order/faction matrix axes and expected observable effects.
- `docs/roadmap/implementation-checklist.md:278-282` — checklist ACs for command coverage.
- `src/OpenSage.Mods.Generals/Gui/GeneralsControlBar.cs:359-531` — control-bar command-set and special-power button wiring.
- `src/OpenSage.Mods.Generals/Gui/GeneralsControlBar.cs:558-640` — selected-unit command and production-queue wiring.
- `src/OpenSage.Mods.Generals/Gui/CommandButtonUtils.cs:15-37` — `CommandButton` callback entry point.
- `src/OpenSage.Game/Gui/CommandButtonCallback.cs:27-190` — `CommandType` to `OrderType` mapping for baseline actions.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:73-430` and `:437-562` — handled `OrderType` cases and default unimplemented branch.
- `src/OpenSage.Game/Logic/SelectionSystem.cs:113-165` — selection and rally-point APIs used by command assertions.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-34` — headless test base; tests must run in CI, not only as skipped game-file tests.

## Steps

1. Add `src/OpenSage.Game.Tests/Tools/ReplaySketch/P6CommandCoverageTests.cs` as a headless `[Fact]` test file using `MockedGameTest` or the existing non-interactive ReplaySketch test style.
2. In the test file, define a table with one row per baseline control-bar action: move, attack object, force-attack ground, build, produce, sell, repair, ability/special-power, and rally point.
3. For each row, record the source `CommandType` or order-generator path, expected `OrderType`, and expected observable effect. Preserve all existing callback branches and guards if you refactor helpers.
4. Assert every expected `OrderType` has a non-default handler in `OrderProcessor`; if one is missing, make the test fail with that exact action/order name instead of silently skipping it.
5. For implemented rows, author the smallest harness command/assertion needed to prove the effect: movement target set, target health decreases, object/building created or queued, money refunded/withdrawn, repair target set, special-power effect observed, or rally point set.
6. Document pass/fail in the test data/report so each unhandled `OrderType` is listed as an explicit gap.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then verify it is clean.

## Files likely to change

- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P6CommandCoverageTests.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` only if an observable assertion needed by this card is missing
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionEvaluator.cs` only if that assertion must be evaluated

## Acceptance criteria

- [ ] Every baseline control-bar action maps to an expected `OrderType`.
- [ ] Each mapped `OrderType` is proven to have a real `OrderProcessor` handler, not the default unimplemented branch.
- [ ] A harness command per action produces the expected observable effect.
- [ ] The coverage matrix is documented with pass/fail per action and lists unhandled order types as explicit gaps.
- [ ] Tests run in CI as non-skipped `[Fact]` tests; no gate is satisfied only by `[GameFact]` or an Interactive-only test.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P6CommandCoverageTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement missing order handlers in this card; surface them as explicit matrix failures/gaps.
- Do not remove callback branches or guards while extracting test helpers.
- Do not depend on local game installations for the required gate; asset-backed Interactive coverage may be additive only.
- Do not create the P7 "Fill gaps surfaced by P7" card here.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P6 command coverage item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` listing confirmed handlers and explicit gaps.
- Confirm `src/OpenSage.Game.Tests/Tools/ReplaySketch/P6CommandCoverageTests.cs` was created and runs in CI.
- Do not mark Track P P6 `Done` unless all P6 cards are complete.

## Result

<!-- append after completing -->
