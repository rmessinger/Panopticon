# Task Card: `WI-P3-06` — Time-limit score decision

| Field | Value |
|---|---|
| Card ID | `WI-P3-06` |
| Roadmap phase | [Track P · P3](../../track-p-playable-baseline.md) — sub-phase P3-6, see [deep dive §2.5](../../deep-dives/victory-defeat.md#25-sudden-death-and-time-limit-implementation-notes) and [§4](../../deep-dives/victory-defeat.md#4-sub-phases) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P3-01`, `WI-P3-02`, and `WI-P3-04` |
| Diagrams | [P3 Architecture Diagrams](../../deep-dives/victory-defeat.md#8-architecture-diagrams) |

## Goal

Add an optional frame-count time limit to `VictorySystem` that decides the match by a configurable score metric, including clear-winner and tied/shared-victory cases.

## Context to read (and only this)

- `docs/roadmap/deep-dives/victory-defeat.md:68-72` — time-limit design in defeat detection.
- `docs/roadmap/deep-dives/victory-defeat.md:93-103` — frame-limit and score-decision implementation notes.
- `docs/roadmap/implementation-checklist.md:225-228` — score-decision AC.
- `src/OpenSage.Game/IGame.cs:93-96` — map time surface if implementation uses elapsed game time.
- `src/OpenSage.Game/Game.cs:899-927` and `src/OpenSage.Game/Game.cs:963-975` — logic tick timing and update flow.
- `src/OpenSage.Game/Logic/GameLogic.cs:29-32` and `src/OpenSage.Game/Logic/GameLogic.cs:250-292` — current logic frame state and increment.
- `src/OpenSage.Game/Logic/Player.cs:126-134` and `src/OpenSage.Game/Logic/Player.cs:807-839` — player counts available for simple score metrics.
- `src/OpenSage.Game/Logic/Player.cs:1172-1249` — existing score manager persistence fields; preserve all persist branches if exposing any score data.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-70` — headless test base; tests must run in CI.

## Steps

1. Add optional time-limit configuration to the same rule/config seam as the defeat rule. The default must be disabled so existing matches remain untimed.
2. Check the frame-count limit at the `VictorySystem` cadence using logic frames, not wall-clock time. State and test the exact default/sentinel value for "no limit".
3. Add a configurable score metric with a simple default suitable for headless tests, such as buildings standing from `Player.BuildingCount`; keep more advanced metrics behind the seam.
4. On expiry, compute each active side's score, set `GameOutcome.Status = Decided`, `Reason = ScoreDecision`, `DecidedOnFrame`, and the highest-scoring `WinningPlayer`/`WinningTeam`.
5. Cover ties as shared victory/winner-list or winning-team according to the outcome representation established by `WI-P3-01`; do not silently pick a random winner.
6. Add `src/OpenSage.Game.Tests/Logic/Victory/TimeLimitScoreDecisionTests.cs` with headless `[Fact]` tests for disabled-by-default, clear winner, and tied/shared-victory outcomes.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/VictorySystem.cs`
- `src/OpenSage.Game/Logic/GameOutcome.cs`
- `src/OpenSage.Game/Logic/VictoryConditionRule.cs` (or equivalent rule/config seam)
- `src/OpenSage.Game.Tests/Logic/Victory/TimeLimitScoreDecisionTests.cs` (new)

## Acceptance criteria

- [ ] With the default configuration, no time-limit score decision occurs.
- [ ] A scenario with a configured frame-count limit expires and sets `GameOutcome.Status = Decided` and `Reason = ScoreDecision`.
- [ ] A clear highest-score side sets the correct `WinningPlayer`/`WinningTeam` per the configured metric.
- [ ] A tied score produces a shared-victory representation and is covered by a test.
- [ ] The frame-limit sentinel/default is named and verified end-to-end.
- [ ] Headless `[Fact]` tests are not skipped and do not require game files.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~TimeLimitScoreDecisionTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change defeat or `EndGame` semantics except to share the common decided-outcome transition.
- Do not use wall-clock time for the limit.
- Do not expose private score fields by changing persistence order unless absolutely necessary; preserve every existing persist branch and skipped-byte count.
- If the outcome type cannot represent shared victory, mark `Blocked` and create a follow-up instead of picking an arbitrary winner.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Time-limit → score decision" item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` with the time-limit metric and tests run.
- Confirm the required test file `src/OpenSage.Game.Tests/Logic/Victory/TimeLimitScoreDecisionTests.cs` was created and runs in CI.

## Result

<!-- append after completing -->
