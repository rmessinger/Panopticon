# Task Card `WI-P3-01`: GameOutcome and VictorySystem scaffold

| Field | Value |
|---|---|
| Card ID | `WI-P3-01` |
| Roadmap phase | [Track P · P3](../../track-p-playable-baseline.md): sub-phase P3-1, see [deep dive §1.1](../../deep-dives/p3-victory-defeat.md#11-a-gameoutcome-value-the-queryable-result) and [§4](../../deep-dives/p3-victory-defeat.md#3-sub-phases) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | P1 player creation/relationships should remain intact; no P3 card dependency |
| Diagrams | [P3 Architecture Diagrams](../../deep-dives/p3-victory-defeat.md#7-architecture-diagrams) |

## Goal

Add the first-class `GameOutcome` value and a no-decision `VictorySystem : GameSystem` scaffold ticked from the logic loop. During play, `GameOutcome` must be queryable from `GameLogic`/`IGame` and report an explicit in-progress default.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p3-victory-defeat.md:19-46`: `GameOutcome`, `OutcomeStatus`, `OutcomeReason`, and default outcome semantics.
- `docs/roadmap/deep-dives/p3-victory-defeat.md:118-127`: P3 sub-phase boundaries.
- `docs/roadmap/deep-dives/p3-victory-defeat.md:182-232`: P3 architecture diagram types.
- `docs/roadmap/implementation-checklist.md:201-204`: checklist ACs for the scaffold.
- `src/OpenSage.Game/GameSystem.cs:1-27`: how `GameSystem` instances self-register and initialize.
- `src/OpenSage.Game/IGame.cs:23-33` and `src/OpenSage.Game/IGame.cs:119-123`: interface surface for systems and `GameLogic`.
- `src/OpenSage.Game/Game.cs:598-633`: existing system construction/init order; preserve it when adding `VictorySystem`.
- `src/OpenSage.Game/Logic/GameLogic.cs:12-69` and `src/OpenSage.Game/Logic/GameLogic.cs:250-292`: `GameLogic` construction and the logic update loop.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:72-79`: current placeholder outcome paths.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-70`: headless test base; use `[Fact]`, not skipped tests.

## Steps

1. Add `GameOutcome`, `OutcomeStatus`, and `OutcomeReason` under the game logic namespace. Make defaults explicit: `OutcomeStatus.InProgress` must be the zero/default status, and the default reason must not accidentally mean `Eliminated`, `Surrendered`, or `ScoreDecision` during play.
2. Expose the current outcome from `GameLogic` and `IGame` so engine code and headless assertions can query it without casting to `Game`.
3. Add a `VictorySystem : GameSystem` scaffold with a named default check cadence. It should tick from the logic loop and keep an observable tick count or test seam, but it must not decide matches yet.
4. Wire `VictorySystem` into the existing game/system lifecycle without breaking existing `GameSystem.Initialize()`, scene-change, or `GameLogic.Update()` behavior.
5. Add a headless `[Fact]` test file `src/OpenSage.Game.Tests/Logic/Victory/GameOutcomeTests.cs` that asserts: initial outcome is in progress; default reason/sentinel is correct; the scaffold ticks over N logic updates.
6. Update `OutcomeAssertion`/path resolution only enough to read the real `GameOutcome.Status` and default reason; no win/lose logic yet.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/GameOutcome.cs` (new)
- `src/OpenSage.Game/Logic/VictorySystem.cs` (new)
- `src/OpenSage.Game/Logic/GameLogic.cs`
- `src/OpenSage.Game/IGame.cs`
- `src/OpenSage.Game/Game.cs`
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs`
- `src/OpenSage.Game.Tests/Logic/Victory/GameOutcomeTests.cs` (new)

## Acceptance criteria

- [ ] `GameOutcome` is queryable off both `GameLogic` and `IGame`.
- [ ] During play, the default outcome reads `Status = OutcomeStatus.InProgress` and the default reason/sentinel is explicitly verified not to represent a decided outcome.
- [ ] `VictorySystem : GameSystem` is registered and ticks from the logic loop at the named cadence; a test asserts it runs.
- [ ] No match can become `Decided` in this scaffold card.
- [ ] A headless `[Fact]` test runs in CI, is not skipped, and does not require game files.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GameOutcomeTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement defeat, win, surrender, sudden-death, or time-limit decisions here.
- Do not scan all objects for victory state in this scaffold.
- Preserve every existing branch and guard if touching `GameLogic.Update()` or `Game` lifecycle code.
- If the existing game construction order makes `VictorySystem` unsafe to initialize, mark `Blocked` instead of moving unrelated systems.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Add a `GameOutcome` type + a `VictorySystem : GameSystem` ticked from the logic loop" item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` with the outcome API and test name.
- Confirm the required test file `src/OpenSage.Game.Tests/Logic/Victory/GameOutcomeTests.cs` was created and runs in CI.

## Result

- Added `GameOutcome`, `OutcomeStatus`, and `OutcomeReason` in `OpenSage.Logic`, exposed the live outcome via `IGame`/`GameLogic`, and wired a scaffold `VictorySystem : GameSystem` into the logic loop with a named cadence and no decision logic.
- Added headless coverage in `src/OpenSage.Game.Tests/Logic/Victory/GameOutcomeTests.cs` for default in-progress outcome semantics and cadence ticks; validation passed with the repo-local `.dotnet` SDK.
