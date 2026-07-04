# Task Card: `WI-P3-04` — End-of-match flow and EndGame handling

| Field | Value |
|---|---|
| Card ID | `WI-P3-04` |
| Roadmap phase | [Track P · P3](../../track-p-playable-baseline.md) — sub-phase P3-4, see [deep dive §2.4](../../deep-dives/victory-defeat.md#24-end-of-match-flow) and [§4](../../deep-dives/victory-defeat.md#4-sub-phases) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-P3-01`; `WI-P3-02` for eliminated outcomes; P1 relationships must remain available |
| Diagrams | [P3 Architecture Diagrams](../../deep-dives/victory-defeat.md#8-architecture-diagrams) |

## Goal

Complete the decided-match flow: a decided `GameOutcome` relaxes/stops simulation for headless use, `OrderType.EndGame` produces a surrender outcome, and outcome assertions can read winner/loser/reason/frame.

## Context to read (and only this)

- `docs/roadmap/deep-dives/victory-defeat.md:82-91` — decided vs sudden-death end-of-match behavior.
- `docs/roadmap/implementation-checklist.md:212-220` — match-end, `EndGame`, and queryable-outcome ACs.
- `src/OpenSage.Game/Logic/Orders/OrderType.cs:1-6` — `OrderType.EndGame` value.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:24-43` — player resolution and order switch.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:553-566` — current default "Unimplemented order type" path and processed-order event.
- `src/OpenSage.Game/IGame.cs:80-96` — `InGame`, `IsLogicRunning`, and `MapTime` surface.
- `src/OpenSage.Game/Game.cs:899-927` — logic/scripting update gating via `IsLogicRunning`.
- `src/OpenSage.Game/Game.cs:963-975` — game logic tick location.
- `src/OpenSage.Game/Game.cs:1056-1060` — existing exit method.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:169-189` — placeholder outcome assertion implementation.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs:228-232` — blocked P0 outcome acceptance test.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-70` — headless test base; tests must run in CI.

## Steps

1. Add a single `VictorySystem`/`GameLogic` transition method for setting a decided outcome. It must set status, reason, winner/loser/team, and decided frame exactly once.
2. When status transitions to `Decided`, stop or relax simulation for headless runs using the existing logic-running surface. Keep render/UI timers usable; do not call full process exit.
3. Handle `OrderType.EndGame` in `OrderProcessor`: resolve the issuing player, mark that player as surrendered/lost, compute the winning non-allied side, and set `GameOutcome.Reason = Surrendered`.
4. Ensure `EndGame` no longer reaches the default "Unimplemented order type" log path.
5. Wire `OutcomeAssertion` and assertion path resolution to read real `GameOutcome.Status`, `Reason`, winning player/team, losing player, and decided frame.
6. Add `src/OpenSage.Game.Tests/Logic/Victory/EndOfMatchFlowTests.cs` with headless `[Fact]` tests for eliminated outcome flow, `EndGame` surrender flow, and outcome assertion failure on wrong expected result.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/VictorySystem.cs`
- `src/OpenSage.Game/Logic/GameOutcome.cs`
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs`
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs`
- `src/OpenSage.Game.Tests/Logic/Victory/EndOfMatchFlowTests.cs` (new)
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs`

## Acceptance criteria

- [ ] When all non-allies of player X are defeated, the decided outcome from `WI-P3-02` stops/relaxes simulation and remains queryable.
- [ ] An `EndGame` order produces `GameOutcome.Status = Decided` and `Reason = Surrendered` for the issuing player instead of logging "Unimplemented order type".
- [ ] `GameOutcome` exposes winner, loser, reason, and decided frame for headless assertion.
- [ ] The P0 harness/outcome assertion can assert on the decided result and fails loudly for a wrong expected result.
- [ ] Exit back to menu/clean headless termination remains possible; no hard process exit is introduced.
- [ ] Headless `[Fact]` tests are not skipped and do not require game files.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~EndOfMatchFlowTests|FullyQualifiedName~OutcomeAssertion_MatchDecided_ReportsPass"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement sudden-death or time-limit score-decision logic here.
- Do not remove or reorder unrelated `OrderProcessor` cases; preserve every existing branch and guard.
- Do not satisfy the acceptance gate with only a `[GameFact]`, skipped, or Interactive test.
- If UI win/lose dialog work is too large, expose a minimal event/state hook and document a follow-up; headless outcome flow is required.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Win/lose determination + match-end flow", "Handle the `EndGame` order", and "Expose match outcome as a first-class queryable result" items in [`implementation-checklist.md`](../../implementation-checklist.md) as applicable.
- Append a result line to repo-root `.exchange` with the decided-flow and `EndGame` tests run.
- Confirm the required test file `src/OpenSage.Game.Tests/Logic/Victory/EndOfMatchFlowTests.cs` was created and runs in CI.

## Result

<!-- append after completing -->
