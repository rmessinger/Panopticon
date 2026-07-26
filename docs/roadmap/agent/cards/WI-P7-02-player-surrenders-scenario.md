# Task Card `WI-P7-02`: PlayerSurrenders regression scenario

| Field | Value |
|---|---|
| Card ID | `WI-P7-02` |
| Roadmap phase | [Track P · P7](../../track-p-playable-baseline.md): P7-2 PlayerSurrenders, see [deep dive §6](../../deep-dives/p7-end-to-end-regression.md#6-sub-phases) and [§2](../../deep-dives/p7-end-to-end-regression.md#2-the-six-end-state-scenarios) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | Track P phases `P0`, `P2`, `P3`, `P4`, `P5`, `P6`, `P6b` complete; `WI-P3-04` done for `OrderType.EndGame` surrender/end-of-match flow |
| Diagrams | [P7 Architecture Diagrams](../../deep-dives/p7-end-to-end-regression.md#8-architecture-diagrams) |

## Goal

Create the checked-in P7 PlayerSurrenders scenario where the human player issues `OrderType.EndGame` after brief play and the run proves `Decided/Surrendered` with the correct winner/loser.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:38-45`: PlayerSurrenders expected outcome.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:47-54`: deterministic scenario authoring requirements.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:58-70`: universal invariants and per-tick abort behavior.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:112-112`: P7-2 sub-phase row.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:298-300`: `OrderType.EndGame` dependency on P3-4.
- `docs/roadmap/implementation-checklist.md:337-344`: scenario and outcome ACs.
- `src/OpenSage.Game/Logic/Orders/OrderType.cs:3-6`: `EndGame` enum value.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:426-428`: `EndGame` processing path.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ReplayScenario.cs:6-22`: scenario JSON shape.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandSpec.cs:86-96`: open command creation.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs:32-127`: run-and-assert entry point.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:169-189`: outcome assertion surface to wire to P3 `GameOutcome`.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:226-242`: final P7 matrix consumer.

## Steps

1. Add `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/player-surrenders.json` as a static deterministic `ReplayScenario`.
2. Include a short fixed-frame opening sequence that proves the match booted, then emit `OrderType.EndGame` for the human player via the P0 open command model.
3. Assert the P3 `GameOutcome` is `OutcomeStatus.Decided`, `OutcomeReason.Surrendered`, with the surrendering human as loser and the opposing side as winner. Verify default/sentinel values do not pass.
4. Add or extend focused P7 scenario test wiring so this scenario is loaded from the JSON and run through `HarnessRunner.RunAndAssert` with a real failing exit code on mismatch.
5. Attach available universal invariants without changing their semantics; P7-07 will ensure all three are attached to every scenario.
6. If any new `.cs` file is created, ensure it is UTF-8 with BOM. Run `dotnet format src`, then verify `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/player-surrenders.json` (new)
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P7RegressionScenarioTests.cs` (new or updated focused test wiring)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` (only if the P3 outcome assertion path is still placeholder-backed)

## Acceptance criteria

- [ ] `player-surrenders.json` exists as static checked-in scenario data.
- [ ] The scenario emits a human `OrderType.EndGame` through the ReplaySketch/P0 command path, not by directly mutating `GameOutcome`.
- [ ] The scenario terminates with `OutcomeStatus.Decided` and `OutcomeReason.Surrendered`.
- [ ] Winner and loser assertions identify the non-surrendering side and surrendering human player respectively.
- [ ] The focused test is a real non-skipped pass/fail gate; failures are not silent skips.
- [ ] Any applicable universal invariants run per tick and report frame numbers on failure.
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

- Do not implement P3-4 surrender handling here; block if `OrderType.EndGame` does not produce a queryable surrendered outcome.
- Do not call `Game.EndGame()` directly from the test as a shortcut; the scenario must issue the order.
- Do not author AI surrender, elimination, team-win, mutual-elimination, or score-decision scenarios here.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the relevant P7 checklist item(s) in [`implementation-checklist.md`](../../implementation-checklist.md) only if this card completes that item.
- Append a result line to repo-root `.exchange`.
- Confirm `player-surrenders.json` and its focused test wiring were created.

## Result

<!-- append after completing -->
