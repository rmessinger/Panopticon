# Task Card `WI-P5-01`: Player + AI building placement

| Field | Value |
|---|---|
| Card ID | `WI-P5-01` |
| Roadmap phase | [Track P · P5](../../track-p-playable-baseline.md): sub-phase P5-1, see [deep dive §4](../../deep-dives/p2-p5-skirmish-ai.md#3-sub-phases), [§1.4](../../deep-dives/p2-p5-skirmish-ai.md#14-p5-construction-and-production-completeness), and [Appendix](../../deep-dives/p2-p5-skirmish-ai.md#how-actions-are-actuated-the-p5-mechanism) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | P1 and P4 complete |
| Diagrams | [P5 Architecture Diagrams](../../deep-dives/p2-p5-skirmish-ai.md#7-architecture-diagrams) |

## Goal

Verify and close the shared player/AI building-placement path: `OrderProcessor.BuildObject` creates a structure, assigns a builder via `IBuilderAIUpdate`, and the building reaches completed construction from a valid location.

## Context to read (and only this)

- `docs/roadmap/implementation-checklist.md:294-296`: checklist item and exact AC to satisfy.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:60-69`: P5 gap-closing scope.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:84-93`: P5-1/P5-2 sub-phase mapping.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:252-258`: P5 action path through orders and builder AI.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:272-274`: builder placement mechanism.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:96-121`: `BuildObject` handler, debit, object creation, and builder target assignment.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:123-130`: cancel/refund path to preserve.
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/IBuilderAIUpdate.cs:12-18`: builder interface contract.
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/DozerAIUpdate.cs:41-46`: dozer build-target wiring.
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/WorkerAIUpdate.cs:48-54`: worker build-target wiring.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:991-1082`: construction lifecycle and completion event.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:1298-1316`: builder cancellation when a construction target dies.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-34`: headless test base.

## Steps

1. Add `src/OpenSage.Game.Tests/Logic/Construction/BuildingPlacementTests.cs` as a headless `[Fact]` test file using `MockedGameTest`; ensure the new `.cs` file is UTF-8 with BOM.
2. Verification path first: issue a valid `BuildObject` order for a human player and the same order for an AI-owned player through `OrderProcessor`; do not call `CreateObject`, `PrepareConstruction`, or `SetBuildTarget` directly from the test.
3. Assert both orders withdraw the expected build cost, create a player-owned structure at the requested valid position/angle, assign a dozer/worker `IBuilderAIUpdate.BuildTarget`, and progress to `BuildProgress >= 1.0`.
4. Assert cancel/refund and destroyed-target cleanup still work for the same path; preserve every existing guard/branch if refactoring is needed.
5. Fallback implement-if-missing: if either player type cannot construct through the order path, make the smallest change in `OrderProcessor.BuildObject` or the builder `AIUpdate` path so both human and AI orders share the same behavior. Do not add a direct AI-only construction API.
6. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs`
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/DozerAIUpdate.cs`
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/WorkerAIUpdate.cs`
- `src/OpenSage.Game.Tests/Logic/Construction/BuildingPlacementTests.cs` (new)

## Acceptance criteria

- [ ] Both a player order and an AI request place and construct a structure to completion at a valid location; harness asserts a finished building for each.
- [ ] Both paths enter through `OrderProcessor.BuildObject` and assign a builder via `IBuilderAIUpdate`; no direct construction-only shortcut is used for AI.
- [ ] Build-cost debit, cancel/refund, ownership, and destroyed-target cleanup remain covered and unchanged.
- [ ] Defaults/sentinels used by the test are explicit and asserted, including starting money and expected final `BuildProgress`.
- [ ] The new headless `[Fact]` runs in CI and does not require game files.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~BuildingPlacementTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement strategic AI planning or build-list selection here; the AI side is a scripted/order-path request only.
- Do not bypass `OrderProcessor` with direct construction APIs.
- Do not weaken money, owner, builder, or construction-completion guards to make the test pass.
- If no valid headless map/terrain setup can support placement, mark `Blocked` with the exact missing seam rather than using a skipped `[GameFact]`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Player and AI building placement (`OrderProcessor.BuildObject` + `IBuilderAIUpdate`)" P5 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `src/OpenSage.Game.Tests/Logic/Construction/BuildingPlacementTests.cs` was created and runs in CI.

## Result

Implemented on branch `track-p5-construction-production` alongside WI-P5-02..05 (see `.exchange` 2026-07-22 entry). Added `src/OpenSage.Game.Tests/Logic/Construction/BuildingPlacementTests.cs` (5 non-skipped `[Fact]`s: human/AI `BuildObject` order path, cost debit, builder assignment, construction completion, cancel/refund). Required headless test-host enablers: `GameLogic.CreateObject` null-guards for `Scene3D`/`PartitionCellManager`, `OrderProcessor` guarded `GameObjects` helper, `AIUpdate.AppendPathToTargetPoint` null-guard, `GameObject`/`ProductionUpdate`/`DozerAndWorkerState` guarded model-condition-flag setters for a null `Drawable`. `dotnet build src` and `dotnet test src --filter Category!=Interactive` passed (781 passed, 0 failed); `dotnet format src --verify-no-changes` clean.

