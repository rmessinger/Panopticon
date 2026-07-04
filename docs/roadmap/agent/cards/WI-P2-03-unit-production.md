# Task Card: `WI-P2-03` — Unit production

| Field | Value |
|---|---|
| Card ID | `WI-P2-03` |
| Roadmap phase | [Track P · P2](../../track-p-playable-baseline.md) — sub-phase P2-3, see [deep dive §4](../../deep-dives/skirmish-ai.md#4-sub-phases) and [§2.1](../../deep-dives/skirmish-ai.md#21-add-a-strategic-decision-loop-to-the-skirmish-ai) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P2-02`; P5 production/funding/prerequisite behavior available for `CreateUnit` |
| Diagrams | [P2 Architecture Diagrams](../../deep-dives/skirmish-ai.md#8-architecture-diagrams) |

## Goal

Add the minimum AI production planner: from existing production structures, queue affordable, prerequisite-satisfied army units through `OrderProcessor` until a small target force exists.

## Context to read (and only this)

- `docs/roadmap/deep-dives/skirmish-ai.md:31-38` — production planner and budget/idempotence rules.
- `docs/roadmap/deep-dives/skirmish-ai.md:252-263` — `OrderProcessor` and `ProductionUpdate` production path.
- `docs/roadmap/deep-dives/skirmish-ai.md:268-271` — prerequisite and tech gating.
- `src/OpenSage.Game/Logic/Player.cs:363-427` — `Player.CanProduceObject(...)` prerequisite/science checks.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:1164-1192` — purchase and unit-construction gating.
- `src/OpenSage.Game/Logic/Object/Update/ProductionUpdate.cs:271-291` — producer-local production gating.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:179-204` — `CreateUnit` order handler and producer fallback.
- `src/OpenSage.Game/Logic/AI/SkirmishAIPlayer.cs:3-31` — AI subclass to extend from the prior cards.
- `src/OpenSage.Game/Logic/Player.cs:130-134` and `:807-839` — owned-object counts used to measure force size.

## Steps

1. Add a production step after economy/base build-out in the AI decision hook. The default target army size for this card is `3` attack-capable, non-builder units; define it as a named constant and verify that default in tests.
2. Identify AI-owned production structures from `Player.OwnedObjects`. Skip incomplete structures, structures without `ProductionUpdate`, and structures that cannot enqueue.
3. Select a legal unit to produce from the structure's command set or existing production metadata. Keep the first implementation minimal and deterministic; if a richer target-composition design is needed, stop at the seam and point to the deep dive rather than inventing a strategy.
4. Before emitting `CreateUnit`, require all gates to pass: `Player.CanProduceObject(...)`, producer `CanConstructUnit(...)`/`CanProduceObject(...)`, no duplicate queued job for that unit beyond the target count, and `BankAccount` can afford the cost.
5. Emit unit production only as `OrderType.CreateUnit` through the AI order-actuation seam from `WI-P2-02`; do not call `ProductionUpdate.QueueProduction(...)` directly from the planner.
6. Add `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIUnitProductionTests.cs` with headless `[Fact]` tests. Cover both affordable production and an unaffordable case where no order is emitted and money never goes negative.
7. New `.cs` files must be UTF-8 with BOM. After creating files, run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/AI/SkirmishAIPlayer.cs`
- `src/OpenSage.Game/Logic/AI/AIPlayer.cs` (only if shared planner state/seams belong in the base)
- `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIUnitProductionTests.cs` (new)

## Acceptance criteria

- [ ] The AI produces at least 3 army units in a headless run when money, prerequisites, and a producer are available.
- [ ] Production is gated by `Player.CanProduceObject(...)` and producer-local construction/production checks.
- [ ] When the selected unit is unaffordable, no `CreateUnit` order is emitted and `BankAccount` never becomes negative.
- [ ] Production orders are emitted through `OrderProcessor`/the recorded order stream, not by direct `ProductionUpdate` calls.
- [ ] The default target force size is named, documented or obvious in code, and verified by a test.
- [ ] Tests are `[Fact]`/CI-runnable or otherwise fail loudly; no skipped test is the only acceptance gate.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SkirmishAIUnitProductionTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement attack/defense behavior here.
- Do not hard-code a full faction strategy table. A deterministic "first legal army unit from an owned producer" is enough for Track P unless the deep dive is updated.
- If you refactor producer-selection logic out of `OrderProcessor`, preserve every existing fallback and guard.
- Any new sentinel such as "no producer" or "no selected unit" must have a stated default and a test that exercises it.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "AI unit production" P2 item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIUnitProductionTests.cs` was created and the tests run in CI.

## Result

<!-- append after completing -->
