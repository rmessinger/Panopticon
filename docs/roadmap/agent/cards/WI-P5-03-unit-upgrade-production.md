# Task Card: `WI-P5-03` — Unit and upgrade production

| Field | Value |
|---|---|
| Card ID | `WI-P5-03` |
| Roadmap phase | [Track P · P5](../../track-p-playable-baseline.md) — sub-phase P5-2, see [deep dive §4](../../deep-dives/p2-p5-skirmish-ai.md#3-sub-phases), [§1.4](../../deep-dives/p2-p5-skirmish-ai.md#14-p5-construction-and-production-completeness), and [Appendix](../../deep-dives/p2-p5-skirmish-ai.md#how-actions-are-actuated-the-p5-mechanism) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | P1 and P4 complete |
| Diagrams | [P5 Architecture Diagrams](../../deep-dives/p2-p5-skirmish-ai.md#7-architecture-diagrams) |

## Goal

Verify and close structure production end-to-end: units queued through `ProductionUpdate.QueueProduction(...)` spawn and move to rally points, and upgrades queued through `QueueUpgrade(...)` complete and apply their player/object effect.

## Context to read (and only this)

- `docs/roadmap/implementation-checklist.md:264-266` — checklist item and exact AC to satisfy.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:60-69` — P5 production and upgrade scope.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:84-93` — P5-2 upgrades row.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:257-263` — queue/spawn mechanism.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:275-277` — upgrade completion/application mechanism.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:148-168` — upgrade order, debit, cancel, and refund.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:179-224` — unit order, producer selection, queueing, cancel, and refund.
- `src/OpenSage.Game/Logic/Object/Update/ProductionUpdate.cs:59-156` — queue advancement and upgrade grant.
- `src/OpenSage.Game/Logic/Object/Update/ProductionUpdate.cs:343-430` — unit creation, ownership, spawn transform, natural rally point, and explicit rally point.
- `src/OpenSage.Game/Logic/Object/Update/ProductionUpdate.cs:472-549` — `QueueProduction`, `CancelProduction`, `QueueUpgrade`, and `CancelUpgrade`.
- `src/OpenSage.Game/Logic/Object/Update/ProductionUpdate.cs:551-551` — queue-capacity sentinel.
- `src/OpenSage.Game/Logic/Object/UpgradeTemplate.cs:130-145` — player/object upgrade application.
- `src/OpenSage.Game/Logic/Player.cs:429-498` — player upgrade queue/completion state.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:1232-1247` — object upgrade application and module updates.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-34` — headless test base.

## Steps

1. Add `src/OpenSage.Game.Tests/Logic/Production/UnitUpgradeProductionTests.cs` as a headless `[Fact]` test file; ensure the new `.cs` file is UTF-8 with BOM.
2. Verification path first: use a real producer structure and queue a real unit via the order path or `ProductionUpdate.QueueProduction(...)` only when the order path cannot be reached headlessly.
3. Set a non-default rally point, advance production until the job finishes, and assert the produced unit is owned by the producer owner, spawned from the production exit, and has the rally target queued.
4. Queue a real upgrade, advance production until completion, and assert the expected `Player.HasUpgrade(...)` or object upgrade/module effect is applied.
5. Cover lifecycle paths: unit cancel/refund, upgrade cancel/refund, production queue capacity/default sentinel, and ownership of produced units.
6. Fallback implement-if-missing: if spawn/rally, upgrade application, refund, or queue-capacity behavior is absent, make the smallest change in `ProductionUpdate`, `OrderProcessor`, or upgrade application code while preserving all existing branches and guards.
7. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs`
- `src/OpenSage.Game/Logic/Object/Update/ProductionUpdate.cs`
- `src/OpenSage.Game/Logic/Object/UpgradeTemplate.cs`
- `src/OpenSage.Game/Logic/Player.cs`
- `src/OpenSage.Game/Logic/Object/GameObject.cs`
- `src/OpenSage.Game.Tests/Logic/Production/UnitUpgradeProductionTests.cs` (new)

## Acceptance criteria

- [ ] A queued unit spawns at the rally point and a queued upgrade completes and applies its effect; harness asserts both.
- [ ] Unit and upgrade queue entries debit cost on enqueue and refund correctly on cancel without duplicating or losing ownership.
- [ ] The test asserts queue-capacity/default sentinel behavior for `CanEnqueue()` and fails loudly if production silently ignores it.
- [ ] Existing production, upgrade, cancel, and refund guards are preserved if code moves.
- [ ] The new headless `[Fact]` runs in CI and does not require game files.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~UnitUpgradeProductionTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement broad AI production policy or faction composition here.
- Do not use only a skipped `[GameFact]` to satisfy production coverage.
- Do not remove existing horde, aircraft/parking-place, harvester, or door-animation branches while refactoring spawn logic.
- If a real upgrade cannot be loaded headlessly, mark `Blocked` with the missing data seam rather than asserting a no-op.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Unit/upgrade production from structures, end-to-end (`ProductionUpdate.QueueProduction/QueueUpgrade`)" P5 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `src/OpenSage.Game.Tests/Logic/Production/UnitUpgradeProductionTests.cs` was created and runs in CI.

## Result

<!-- append after completing -->

