# Task Card `WI-P6-03`: Death cleanup

| Field | Value |
|---|---|
| Card ID | `WI-P6-03` |
| Roadmap phase | [Track P · P6](../../track-p-playable-baseline.md): sub-phase P6-3, see [deep dive §P6](../../deep-dives/p4-p6-economy-production-combat.md#p6-combat-and-command-verification) and [§P6 sub-phases](../../deep-dives/p4-p6-economy-production-combat.md#p6-sub-phases) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | P1 owner tracking; share/reuse P3-4's death event from [victory/defeat §1.3](../../deep-dives/p3-victory-defeat.md#13-death--defeat-plumbing) |
| Diagrams | [P6 Architecture Diagrams](../../deep-dives/p4-p6-economy-production-combat.md#architecture-diagrams) |

## Goal

Ensure dying or destroyed objects leave no dangling references: remove them from selection and owner tracking from both `OnDie` and `Destroy`, using the same death notification shared with P3-4.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:82-87`: death cleanup design and P3-4 sharing requirement.
- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:89-112`: P6-3 sub-phase and sequencing dependency.
- `docs/roadmap/deep-dives/p3-victory-defeat.md:74-80`: death→defeat event that P6-3 must share.
- `docs/roadmap/implementation-checklist.md:287-289`: checklist ACs for death cleanup.
- `src/OpenSage.Game/Logic/SelectionSystem.cs:113-165`: selection mutation APIs.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:291`: current `Owner` property shape.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:1284-1366`: `Kill`, `OnDie`, and `Destroy`.
- `src/OpenSage.Game/Logic/GameLogic.cs:180-201`: destroy lifecycle, owner removal, and object-destroyed event.
- `src/OpenSage.Game/IGame.cs:140-144`, `src/OpenSage.Game/Game.cs:128-139`, and `src/OpenSage.Game/Logic/GameObjectDestroyedEventArgs.cs:6-14`: existing object-destroyed event surface.
- `src/OpenSage.Game/Logic/Player.cs:130-134` and `:807-839`: `OwnedObjects`, `BuildingCount`, `UnitCount`, add/remove helpers from P1.
- `src/OpenSage.Game.Tests/Logic/PlayerOwnershipTests.cs:8-35`: ownership-tracking test style.

## Steps

1. Add `src/OpenSage.Game.Tests/Logic/Object/DeathCleanupTests.cs` as a headless `[Fact]` test file using `MockedGameTest`.
2. Identify the shared P3-4 death/destroy notification surface. Reuse or extend it; do not create a parallel event bus.
3. Wire all lifecycle paths: `GameObject.OnDie(...)` and `GameObject.Destroy()`/`GameLogic.DestroyObject(...)` must both remove the object from selection and from `Player.OwnedObjects`.
4. Preserve every existing death branch and guard, including construction death handling, `IsSelectable = false`, die modules, sound playback, and destroy idempotence.
5. Add tests for both paths:
   - selected owned unit dies through `Kill`/`OnDie` and is absent from selected units and owner tracking;
   - selected owned unit is destroyed through `Destroy`/`GameLogic.DestroyObject` and is absent from selected units and owner tracking.
6. Add or preserve assertions that `BuildingCount`/`UnitCount` defaults never go negative and match `OwnedObjects` after cleanup.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then verify it is clean.

## Files likely to change

- `src/OpenSage.Game.Tests/Logic/Object/DeathCleanupTests.cs` (new)
- `src/OpenSage.Game/Logic/Object/GameObject.cs`
- `src/OpenSage.Game/Logic/GameLogic.cs`
- `src/OpenSage.Game/Logic/SelectionSystem.cs`
- `src/OpenSage.Game/Logic/Player.cs` only if P1 ownership removal needs a directly coupled fix

## Acceptance criteria

- [ ] A dying selected unit is removed from selection.
- [ ] A dying selected unit is removed from `Player.OwnedObjects`, and building/unit counts remain correct.
- [ ] The same cleanup works for both `OnDie` and `Destroy` paths.
- [ ] Cleanup reuses the P3-4 death/destroy notification; no duplicate death event is introduced.
- [ ] Tests assert no dangling selected-unit or owner-tracking references after death.
- [ ] Tests run in CI as non-skipped `[Fact]` tests; no gate is satisfied only by `[GameFact]` or an Interactive-only test.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~DeathCleanupTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not redesign ownership or selection; only wire cleanup onto existing lifecycle/event seams.
- Do not remove existing death behavior side effects.
- Do not create a separate P6-only death event; coordinate with P3-4's shared death event.
- Do not implement victory/defeat rules in this card.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P6 death cleanup item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` describing the shared event and both lifecycle paths tested.
- Confirm `src/OpenSage.Game.Tests/Logic/Object/DeathCleanupTests.cs` was created and runs in CI.
- Do not mark Track P P6 `Done` unless all P6 cards are complete.

## Result

Done. Wired cleanup onto both lifecycle seams, reusing the existing `Player.DeselectUnit`/`RemoveOwnedObject` and the shared `GameLogic.ObjectLifecycleChanged` notification (no new event bus):
- `GameObject.OnDie` now calls `Owner?.RemoveOwnedObject(this)` right after the existing `Owner.DeselectUnit(this)` (and before the Died lifecycle event, so VictorySystem defeat evaluation sees correct counts). Previously OnDie deselected but left the object in OwnedObjects.
- `GameLogic.DestroyObject` now calls `owner?.DeselectUnit(gameObject)` alongside the existing `owner?.RemoveOwnedObject(gameObject)`. Previously Destroy removed from ownership but not selection, and Destroy can run without a prior OnDie (Sell/CancelBuild/teardown).
Both removals are idempotent, so the OnDie→Destroy flow double-removes safely and counts never go negative. Added `src/OpenSage.Game.Tests/Logic/Object/DeathCleanupTests.cs` (3 non-skipped `[Fact]`s): OnDie path (Kill a selected unit → absent from selection + OwnedObjects, counts drop by one, bystander untouched), Destroy path (Destroy a selected unit → same), and a mixed-path consistency test (2 buildings + 3 units removed through OnDie/Destroy/idempotent re-removal, counts stay consistent with OwnedObjects and never negative). Validation: dotnet build src (0 errors); DeathCleanupTests (3 passed); Category!=Interactive (760 passed, 15 skipped, 0 failed); dotnet format src --verify-no-changes (clean).

