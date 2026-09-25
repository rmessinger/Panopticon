# Task Card `WI-P3-03`: Death-to-defeat lifecycle event

| Field | Value |
|---|---|
| Card ID | `WI-P3-03` |
| Roadmap phase | [Track P · P3](../../track-p-playable-baseline.md): sub-phase P3-3, see [deep dive §1.3](../../deep-dives/p3-victory-defeat.md#13-death--defeat-plumbing) and [§4](../../deep-dives/p3-victory-defeat.md#3-sub-phases) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-P3-01` and `WI-P1-07`; this card feeds `WI-P3-02` incremental defeat checks |
| Diagrams | [P3 Architecture Diagrams](../../deep-dives/p3-victory-defeat.md#7-architecture-diagrams) |

## Goal

Add a death/destroy/ownership-change notification that lets `VictorySystem` react incrementally to affected players without scanning every object.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p3-victory-defeat.md:74-80`: required event sources and incremental reaction.
- `docs/roadmap/implementation-checklist.md:209-211`: lifecycle-event AC.
- `src/Panopticon.Simulation/Logic/GameLogic.cs:71-128`: object creation and owned-object add hook.
- `src/Panopticon.Simulation/Logic/GameLogic.cs:178-199`: destroy path, owned-object removal, and existing destroyed event.
- `src/Panopticon.Simulation/Logic/Object/GameObject.cs:291-297`: current owner and structure fields.
- `src/Panopticon.Simulation/Logic/Object/GameObject.cs:1281-1337`: `Kill(...)`/`OnDie(...)` death path.
- `src/Panopticon.Simulation/Logic/Object/GameObject.cs:1360-1366`: `Destroy()` path.
- `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs:96-121`: one current owner assignment after construction.
- `src/Panopticon.Simulation/Logic/Object/Contain/GarrisonContain.cs:40-53`: owner transfer through garrisoning.
- `src/Panopticon.Simulation/Logic/Object/Behaviors/CastleBehavior.cs:76-126`: owner transfer through castle behavior.
- `src/Panopticon.Simulation/Logic/Object/Update/ProductionUpdate.cs:352-356`: owner assignment for produced units.
- `src/Panopticon.Simulation/Logic/Player.cs:807-839`: owned-object add/remove idempotence and count maintenance.
- `test/Panopticon.Simulation.Tests/MockedGameTest.cs:24-70`: headless test base; tests must run in CI.

## Steps

1. Add a single lifecycle notification model that includes the affected object, previous owner, current owner, and reason (`Died`, `Destroyed`, `OwnerChanged`, or equivalent).
2. Wire the notification from both `GameObject.OnDie(...)` and `GameObject.Destroy()`/`GameLogic.DestroyObject(...)`. Both paths must be covered; do not assume one always calls the other.
3. Replace direct owner field assignment with a helper/setter that updates `OwnedObjects`/counts and fires owner-change notifications once per actual owner change.
4. Subscribe `VictorySystem` to the notification and queue/re-check only affected players at its cadence.
5. Preserve every existing branch/guard in death, destroy, construction, garrison, castle, and production ownership flows; no behavior loss on refactor.
6. Add `test/Panopticon.Simulation.Tests/Logic/Victory/VictoryObjectLifecycleEventTests.cs` with headless `[Fact]` tests proving: `OnDie` fires once; `Destroy` fires once; ownership change fires once and updates both players' counts; `VictorySystem` reacts without scanning all objects.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/Panopticon.Simulation/Logic/Object/GameObject.cs`
- `src/Panopticon.Simulation/Logic/GameLogic.cs`
- `src/Panopticon.Simulation/Logic/Player.cs`
- `src/Panopticon.Simulation/Logic/VictorySystem.cs`
- `src/Panopticon.Simulation/Logic/GameObjectLifecycleEventArgs.cs` (new, or equivalent)
- `test/Panopticon.Simulation.Tests/Logic/Victory/VictoryObjectLifecycleEventTests.cs` (new)

## Acceptance criteria

- [x] A death/ownership-change notification fires from `OnDie`, from `Destroy`, and from ownership transfer.
- [x] Each notification fires once per lifecycle event; tests fail on duplicate firing.
- [x] `VictorySystem` reacts incrementally to affected players and does not perform a full-object scan.
- [x] `Player.OwnedObjects`, `BuildingCount`, and `UnitCount` remain consistent across create, death, destroy, and owner transfer.
- [x] Headless `[Fact]` tests are not skipped and do not require game files.
- [x] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [x] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~VictoryObjectLifecycleEventTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement final defeat/win decisions beyond notifying `VictorySystem` of affected players.
- Do not replace owned-object tracking with a scan or maintain duplicate counters outside `Player`.
- Do not skip owner-transfer paths; this is the explicit guardrail from the P1 owner-transfer gap.
- If an owner assignment cannot be safely routed through a helper, mark `Blocked` and cite the path.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Death→defeat event from `GameObject.OnDie/Destroy`" item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` listing each lifecycle path tested.
- Confirm the required test file `test/Panopticon.Simulation.Tests/Logic/Victory/VictoryObjectLifecycleEventTests.cs` was created and runs in CI.

## Result

- Added `GameObjectLifecycleEventArgs`/`GameObjectLifecycleEventReason`, raised lifecycle notifications from `OnDie`, `DestroyObject`, and tracked owner transfers, and subscribed `VictorySystem` to queue only affected players for later checks.
- Added `VictoryObjectLifecycleEventTests` covering one-shot die/destroy/owner-change notifications, ownership-count consistency, and incremental `VictorySystem` recheck queuing; full non-interactive validation passed.
