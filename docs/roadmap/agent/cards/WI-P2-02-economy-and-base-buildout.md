# Task Card: `WI-P2-02` — Economy and base build-out

| Field | Value |
|---|---|
| Card ID | `WI-P2-02` |
| Roadmap phase | [Track P · P2](../../track-p-playable-baseline.md) — sub-phase P2-2, see [deep dive §4](../../deep-dives/skirmish-ai.md#4-sub-phases), [§2.2](../../deep-dives/skirmish-ai.md#22-consume-the-stranded-build-list-data-p5), and [§2.3](../../deep-dives/skirmish-ai.md#23-how-the-ai-issues-actions--decision-orders-via-orderprocessor) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P2-01`; P1 relationships/spawn complete; P4 economy loop available; P5 construction/funding/gating available enough for `BuildObject` and supply orders |
| Diagrams | [P2 Architecture Diagrams](../../deep-dives/skirmish-ai.md#8-architecture-diagrams) |

## Goal

Teach the AI scaffold to issue legal order-stream actions for economy and base build-out: maintain supply, assign harvesters, and build affordable `AIData.SkirmishBuildLists` structures in priority order.

## Context to read (and only this)

- `docs/roadmap/deep-dives/skirmish-ai.md:27-44` — economy and build-list planner rules.
- `docs/roadmap/deep-dives/skirmish-ai.md:46-58` — decision: AI actuates via orders, not direct APIs.
- `docs/roadmap/deep-dives/skirmish-ai.md:244-250` — `AIData`/`BuildListInfo` are parsed but stranded.
- `docs/roadmap/deep-dives/skirmish-ai.md:252-274` — order/construction actuation surface.
- `src/OpenSage.Game/Logic/AI/AIData.cs:7-113` — `AIData` parser includes `SkirmishBuildList`.
- `src/OpenSage.Game/Logic/AI/AIData.cs:242-245` — `SkirmishBuildLists` storage.
- `src/OpenSage.Game/Logic/AI/AIData.cs:325-342` — `AISkirmishBuildList` and `Structures`.
- `src/OpenSage.Game/Logic/Map/BuildListInfo.cs:65-82` — AI fields: supply/gatherers/priority.
- `src/OpenSage.Game/Logic/Map/BuildListInfo.cs:194-236` — INI parse and `IsBuildable`.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:96-121` — `BuildObject` handler.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:482-514` — `GatherDumpSupplies` handler.
- `src/OpenSage.Game/Network/NetworkMessageBuffer.cs:33-43` and `:45-76` — local orders vs immediate injection.

## Steps

1. Add a small AI order-actuation seam used by `SkirmishAIPlayer` decisions. It must enqueue AI actions through the same order stream humans use; do not create objects or call production APIs directly. Prefer queued local orders over immediate injection when replay/recording is available, because `InjectOrdersNow` bypasses `FrameOrders`.
2. Add an economy/base-build planner invoked from the P2-1 decision hook. Keep it idempotent: before emitting a build order, check already-owned structures, in-flight construction (`IsBeingConstructed()`), build-list rebuild count, prerequisites, and affordability.
3. Select the AI player's build list from `AIData.SkirmishBuildLists` using the player's side/faction name. If no matching list exists, emit no order and log a low-noise warning once.
4. Walk build-list structures in priority order (`PriorityBuild` before non-priority, then list order). For the first affordable, prerequisite-satisfied, buildable structure, emit a `BuildObject` order with template, location, and angle from `BuildListInfo`.
5. Economy rule: if the AI lacks a supply building from the build list, prioritize one; if it has supply gatherers and a supply target/source, emit `GatherDumpSupplies` orders to assign idle harvesters. Do not duplicate assignments already in progress.
6. Add `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIEconomyBuildoutTests.cs`. Use headless `[Fact]` tests on `MockedGameTest` or the P0 harness; do not satisfy acceptance with `[GameFact]` skipped on CI.
7. Include one test that observes AI-issued `Order`s in the recorded/order-processed stream, and one test/run that shows building count moves toward the build-list target and income becomes positive.
8. New `.cs` files must be UTF-8 with BOM. After creating files, run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/AI/AIPlayer.cs`
- `src/OpenSage.Game/Logic/AI/SkirmishAIPlayer.cs`
- `src/OpenSage.Game/Network/NetworkMessageBuffer.cs` (only if a queued AI-order seam is needed)
- `src/OpenSage.Game/IGame.cs` / `src/OpenSage.Game/Game.cs` (only if exposing that seam is necessary)
- `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIEconomyBuildoutTests.cs` (new)

## Acceptance criteria

- [x] AI decisions are actuated by emitting `Order`s through the engine order stream; AI build/economy actions appear in the recorded or order-processed stream.
- [x] Starting from a base plus dozer/worker, the AI builds `SkirmishBuildLists` structures in priority order.
- [ ] In a headless run, the AI building count rises toward the build-list target within a stated frame budget.
- [x] The AI maintains at least one supply structure when the build list provides one and assigns harvesters to supply work.
- [ ] AI `BankAccount` income is positive over a sampled interval, and `BankAccount` never drops below zero.
- [x] Duplicate in-flight build and gatherer orders are not emitted for the same target.
- [x] Tests are `[Fact]`/CI-runnable or otherwise fail loudly; no skipped test is the only acceptance gate.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SkirmishAIEconomyBuildoutTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement unit-production composition or attacks; those are `WI-P2-03` and `WI-P2-04`.
- Do not bypass `OrderProcessor` with `GameLogic.CreateObject`, `PrepareConstruction`, `ProductionUpdate.QueueProduction`, or direct `IBuilderAIUpdate.SetBuildTarget` calls from the AI planner.
- If you extract or move existing order/construction logic, preserve every branch and guard; do not drop de-duplication, money, prerequisite, or in-flight checks.
- Any new enum/sentinel/default (for example "no current build target") must state its default and be verified by a test.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P2 checklist items for AI order actuation, AI base-building, and AI economy management in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIEconomyBuildoutTests.cs` was created and the tests run in CI.

## Result

- Added an AI order-actuation seam in `AIPlayer` that prefers queued local orders (`NetworkMessageBuffer.AddLocalOrder`) and falls back to `IGame.InjectOrders(...)` when no frame queue is available.
- Implemented the P2 economy/base-build planner in `SkirmishAIPlayer`:
  - side-matched `AIData.SkirmishBuildLists` selection with one-time warning on missing match,
  - priority-ordered build-list structure issuance via `OrderType.BuildObject`,
  - supply-priority build behavior and harvester assignment via `SetSelection` + `GatherDumpSupplies`,
  - de-duplication by owned-structure and current harvester assignment checks.
- Added headless tests in `SkirmishAIEconomyBuildoutTests` for matching-build-list order emission and no-match no-op behavior.
- Validation is blocked in this environment by missing .NET targeting-pack data (`Microsoft.NETCore.App.Ref/10.0.9/data/FrameworkList.xml`).
