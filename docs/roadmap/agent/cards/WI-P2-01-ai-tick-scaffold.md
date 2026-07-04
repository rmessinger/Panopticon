# Task Card: `WI-P2-01` — AI tick scaffold

| Field | Value |
|---|---|
| Card ID | `WI-P2-01` |
| Roadmap phase | [Track P · P2](../../track-p-playable-baseline.md) — sub-phase P2-1, see [deep dive §4](../../deep-dives/skirmish-ai.md#4-sub-phases) and [§2.1](../../deep-dives/skirmish-ai.md#21-add-a-strategic-decision-loop-to-the-skirmish-ai) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | P1 player creation must still create `SkirmishAIPlayer` for non-human skirmish slots; no P4/P5 dependency for this scaffold |
| Diagrams | [P2 Architecture Diagrams](../../deep-dives/skirmish-ai.md#8-architecture-diagrams) |

## Goal

Add a no-behavior strategic AI tick scaffold: `Player.LogicTick()` invokes `SkirmishAIPlayer`/`AIPlayer` at a stated coarse cadence, and a headless test proves the cadence over N frames.

## Context to read (and only this)

- `docs/roadmap/deep-dives/skirmish-ai.md:20-39` — design for the coarse-cadence decision loop and idempotence.
- `docs/roadmap/deep-dives/skirmish-ai.md:228-233` — current gap: AI classes are persistence-only.
- `src/OpenSage.Game/Logic/AI/AIPlayer.cs:8-154` — base AI player persistence; preserve every persist branch and guard.
- `src/OpenSage.Game/Logic/AI/SkirmishAIPlayer.cs:3-31` — skirmish AI subclass with no behavior.
- `src/OpenSage.Game/Logic/Player.cs:84-109` — `AIPlayer` property and current `LogicTick()`.
- `src/OpenSage.Game/Logic/Player.cs:763-775` — creation of `SkirmishAIPlayer` for skirmish AI slots.
- `src/OpenSage.Game/Logic/PlayerManager.cs:145-150` — player tick loop.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-34` — headless test base.

## Steps

1. In `AIPlayer`, add a virtual coarse-cadence update method called from player ticks. State the default cadence as a named constant, e.g. `DefaultDecisionCadenceFrames = 60`, and keep the default deterministic.
2. Implement the scaffold so every player tick advances an internal countdown, calls a protected decision hook only when the countdown reaches zero, then resets to the default cadence. No orders or gameplay decisions are emitted in this card.
3. Override or inherit the scaffold in `SkirmishAIPlayer` without changing the existing `Persist(...)` layout. If you move any persistence code, preserve every existing branch, guard, exception, skipped-byte count, and default value exactly.
4. Wire `Player.LogicTick()` to call `AIPlayer?.Update()` after `Rank.Update()`. Do not tick neutral/human players that have `AIPlayer == null`.
5. Add `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIPlayerTickTests.cs` as a headless `[Fact]` test file. The test should tick an AI player for a fixed number of frames and assert the exact decision-hook count from the stated cadence.
6. New `.cs` files must be UTF-8 with BOM. After creating files, run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/AI/AIPlayer.cs`
- `src/OpenSage.Game/Logic/AI/SkirmishAIPlayer.cs`
- `src/OpenSage.Game/Logic/Player.cs`
- `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIPlayerTickTests.cs` (new)

## Acceptance criteria

- [ ] `SkirmishAIPlayer.Update(...)` or the inherited base update is invoked from `Player.LogicTick()` at the named default cadence.
- [ ] A headless `[Fact]` test asserts the exact decision tick count over N frames; the test is not skipped and does not require game files.
- [ ] Human/neutral players with `AIPlayer == null` do not tick any AI scaffold.
- [ ] No gameplay behavior or orders are emitted by this scaffold.
- [ ] Existing AI persistence behavior is unchanged; every previous guard and skipped-byte count remains intact.
- [ ] Any new default/sentinel value is named, documented in code or test, and verified end-to-end by the cadence test.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SkirmishAIPlayerTickTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement economy, build-list consumption, production, targeting, or attacks here.
- Do not add direct calls to `OrderProcessor` or `IGame.InjectOrders` in this card.
- Do not persist runtime-only cadence counters unless save/load behavior is explicitly required; if it is required, preserve the existing persisted field order.
- If the tick location is ambiguous beyond `Player.LogicTick()`/`PlayerManager.LogicTick()`, stop and mark `Blocked` rather than inventing a new game-system loop.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Add a coarse-cadence `Update(...)` decision loop" P2 item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIPlayerTickTests.cs` was created and the test runs in CI.

## Result

<!-- append after completing -->
