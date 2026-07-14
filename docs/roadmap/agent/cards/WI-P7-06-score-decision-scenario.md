# Task Card `WI-P7-06`: ScoreDecision regression scenario

| Field | Value |
|---|---|
| Card ID | `WI-P7-06` |
| Roadmap phase | [Track P · P7](../../track-p-playable-baseline.md): P7-6 ScoreDecision, see [deep dive §6](../../deep-dives/p7-end-to-end-regression.md#6-sub-phases) and [§2](../../deep-dives/p7-end-to-end-regression.md#2-the-six-end-state-scenarios) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | Track P phases `P0`, `P2`, `P3`, `P4`, `P5`, `P6`, `P6b` complete; `WI-P3-06` done for frame-limit score decisions |
| Diagrams | [P7 Architecture Diagrams](../../deep-dives/p7-end-to-end-regression.md#8-architecture-diagrams) |

## Goal

Create the checked-in P7 ScoreDecision scenario that reaches a small configured frame limit and proves `Decided/ScoreDecision` with the winner selected by the P3 score metric.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:38-45`: ScoreDecision expected outcome.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:47-54`: deterministic scenario authoring requirements.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:58-70`: universal invariants and failure reporting.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:116-116`: P7-6 sub-phase row.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:302-304`: `MatchConfig.FrameLimit` dependency on P3-6.
- `docs/roadmap/implementation-checklist.md:337-344`: scenario and expected outcome ACs.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs:81-119`: update loop and timeout; distinguish harness timeout from match frame limit.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ReplayScenario.cs:6-22`: scenario JSON shape; extend only if P3 added match config fields.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:169-189`: outcome assertion surface; extend for score-decision reason if needed.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:226-242`: final P7 matrix consumer.

## Steps

1. Add `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/score-decision.json` as static deterministic scenario data.
2. Configure the P3-6 `MatchConfig.FrameLimit` to a small explicit value. Verify the default/sentinel frame-limit value still means no score-decision limit and is not accidentally used by this scenario.
3. Script deterministic early play that makes the intended winner have the higher score by the configured frame limit.
4. Assert the match terminates because the configured frame limit expires, not because `HarnessRunner` hit its default timeout.
5. Assert `OutcomeStatus.Decided`, `OutcomeReason.ScoreDecision`, and the expected `WinningPlayer`/`WinningTeam` selected by the score metric. Verify default/sentinel `GameOutcome` values cannot pass.
6. Add or extend focused P7 scenario test wiring so this JSON runs through `HarnessRunner.RunAndAssert` as a real non-skipped pass/fail test.
7. Attach available universal invariants; P7-07 will audit all three invariants across all scenarios.
8. If any new `.cs` file is created, ensure it is UTF-8 with BOM. Run `dotnet format src`, then verify `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/score-decision.json` (new)
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P7RegressionScenarioTests.cs` (new or updated focused test wiring)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ReplayScenario.cs` (only if P3-6 match config needs scenario serialization)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` (only if score-decision outcome paths still need P3 wiring)

## Acceptance criteria

- [ ] `score-decision.json` exists as static checked-in scenario data.
- [ ] The scenario explicitly configures a small `MatchConfig.FrameLimit`; the default/sentinel frame-limit behavior is verified and not relied on.
- [ ] The scenario terminates by match frame limit with `OutcomeStatus.Decided` and `OutcomeReason.ScoreDecision`.
- [ ] The expected winner is asserted using the P3 score metric.
- [ ] The test fails if the harness timeout is reached instead of the match frame limit.
- [ ] The focused test is a real non-skipped pass/fail gate.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P7RegressionScenarioTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement P3-6 score-decision logic here; block if `MatchConfig.FrameLimit` or score metrics are unavailable.
- Do not confuse the P0 harness timeout with the game match frame limit.
- Do not use random scoring inputs; the score winner must be deterministic.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the relevant P7 checklist item(s) in [`implementation-checklist.md`](../../implementation-checklist.md) only if this card completes that item.
- Append a result line to repo-root `.exchange`.
- Confirm `score-decision.json` and its focused test wiring were created.

## Result

<!-- append after completing -->
