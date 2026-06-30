# Task Card: `WI-P7-05` — MutualElimination regression scenario

| Field | Value |
|---|---|
| Card ID | `WI-P7-05` |
| Roadmap phase | [Track P · P7](../../track-p-playable-baseline.md) — P7-5 MutualElimination, see [deep dive §6](../../deep-dives/end-to-end-regression.md#6-sub-phases) and [§2](../../deep-dives/end-to-end-regression.md#2-the-six-end-state-scenarios) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | Track P phases `P0`, `P2`, `P3`, `P4`, `P5`, `P6`, `P6b` complete; `WI-P3-05` done for mutual-elimination sudden death |
| Diagrams | [P7 Architecture Diagrams](../../deep-dives/end-to-end-regression.md#8-architecture-diagrams) |

## Goal

Create the checked-in P7 MutualElimination scenario that reaches `SuddenDeath` after simultaneous elimination, continues running, and then reaches `Decided/Eliminated` after the next elimination.

## Context to read (and only this)

- `docs/roadmap/deep-dives/end-to-end-regression.md:38-45` — MutualElimination expected two-stage outcome.
- `docs/roadmap/deep-dives/end-to-end-regression.md:47-54` — deterministic scenario authoring requirements.
- `docs/roadmap/deep-dives/end-to-end-regression.md:58-70` — universal invariants and per-tick failure behavior.
- `docs/roadmap/deep-dives/end-to-end-regression.md:115-115` — P7-5 sub-phase row.
- `docs/roadmap/deep-dives/end-to-end-regression.md:243-257` — MutualElimination sequence diagram.
- `docs/roadmap/implementation-checklist.md:337-344` — scenario and expected outcome ACs.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/TimingConfig.cs:15-23` — fixed/random timing primitives; use fixed timing only.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ActionEntry.cs:7-20` — action entries for staged commands.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs:81-119` — update loop and timeout behavior.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:169-189` — outcome assertion surface; extend for two-stage outcome if needed.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:226-254` — final P7 matrix and invariant-violation gates.

## Steps

1. Add `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/mutual-elimination.json` as static deterministic scenario data.
2. Script fixed-timing commands so both sides' last command/production structures fall in the same P3 check window and the match reports `OutcomeStatus.SuddenDeath` rather than ending immediately.
3. Script the next elimination after sudden death so the run then terminates as `OutcomeStatus.Decided` and `OutcomeReason.Eliminated`.
4. Add outcome assertions for both lifecycle points: sudden-death transition first, final decided outcome second, with the expected winning/losing side. Do not check only the final frame.
5. Add or extend focused P7 scenario test wiring so this JSON runs through `HarnessRunner.RunAndAssert` and fails loudly if either lifecycle assertion is missing or wrong.
6. Attach available universal invariants; ensure invariant checks still run during the sudden-death continuation window.
7. If any new `.cs` file is created, ensure it is UTF-8 with BOM. Run `dotnet format src`, then verify `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/mutual-elimination.json` (new)
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P7RegressionScenarioTests.cs` (new or updated focused test wiring)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` (only if two-stage outcome assertions need small support)

## Acceptance criteria

- [ ] `mutual-elimination.json` exists as static checked-in scenario data.
- [ ] The scenario first observes `OutcomeStatus.SuddenDeath` after simultaneous elimination.
- [ ] The scenario continues past sudden death and later terminates with `OutcomeStatus.Decided` and `OutcomeReason.Eliminated`.
- [ ] The final winning/losing side is asserted, and default/sentinel outcomes cannot pass.
- [ ] Invariants run every tick before, during, and after the sudden-death window, not only at end-of-match.
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

- Do not implement missing `WI-P3-05` sudden-death logic here; block if the P3 dependency is incomplete.
- Do not assert only the final `Decided` result; the `SuddenDeath` transition is required acceptance.
- Do not use random timing or live AI choices for the simultaneous elimination window.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the relevant P7 checklist item(s) in [`implementation-checklist.md`](../../implementation-checklist.md) only if this card completes that item.
- Append a result line to repo-root `.exchange`.
- Confirm `mutual-elimination.json` and its focused test wiring were created.

## Result

<!-- append after completing -->
