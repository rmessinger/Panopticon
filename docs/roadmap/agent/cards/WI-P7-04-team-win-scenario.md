# Task Card: `WI-P7-04` — TeamWin regression scenario

| Field | Value |
|---|---|
| Card ID | `WI-P7-04` |
| Roadmap phase | [Track P · P7](../../track-p-playable-baseline.md) — P7-4 TeamWin, see [deep dive §6](../../deep-dives/p7-end-to-end-regression.md#6-sub-phases) and [§2](../../deep-dives/p7-end-to-end-regression.md#2-the-six-end-state-scenarios) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | Track P phases `P0`, `P2`, `P3`, `P4`, `P5`, `P6`, `P6b` complete; `WI-P3-02` and `WI-P3-03` done for team win/elimination behavior |
| Diagrams | [P7 Architecture Diagrams](../../deep-dives/p7-end-to-end-regression.md#8-architecture-diagrams) |

## Goal

Create the checked-in P7 TeamWin scenario where both losing-team players are eliminated and the run proves `Decided/Eliminated` with `WinningTeam` set.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:38-45` — TeamWin expected outcome.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:47-54` — deterministic scenario authoring requirements.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:58-70` — universal invariants and per-tick reporting.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:114-114` — P7-4 sub-phase row.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:194-200` — `GameOutcome` fields including `WinningTeam`.
- `docs/roadmap/implementation-checklist.md:337-344` — scenario and expected outcome ACs.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/PlayerSlotConfig.cs:6-24` — player slot `Team` assignment.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ReplayScenario.cs:6-22` — scenario JSON shape.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs:32-127` — run-and-assert entry point.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:169-189` — outcome assertion surface; extend for `WinningTeam` if needed.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:226-242` — final P7 matrix consumer.

## Steps

1. Add `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/team-win.json` as static deterministic scenario data.
2. Configure a minimal multi-player team setup with one winning team and one losing team containing two players. Use fixed start positions and teams.
3. Script deterministic build/attack/elimination commands so both losing-team players lose all command/production structures with no rebuild path.
4. Assert `OutcomeStatus.Decided`, `OutcomeReason.Eliminated`, the correct `WinningTeam`, and both losing-team players. Verify defaults/sentinels cannot satisfy these assertions.
5. Add or extend focused P7 scenario test wiring so this scenario loads from JSON and runs through `HarnessRunner.RunAndAssert` as a real non-skipped pass/fail test.
6. Attach available universal invariants for all participating players; P7-07 will normalize the shared invariant set across all scenarios.
7. If any new `.cs` file is created, ensure it is UTF-8 with BOM. Run `dotnet format src`, then verify `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/team-win.json` (new)
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P7RegressionScenarioTests.cs` (new or updated focused test wiring)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` (only if `WinningTeam`/loser outcome paths still need P3 wiring)

## Acceptance criteria

- [ ] `team-win.json` exists as static checked-in scenario data.
- [ ] The scenario has a deterministic multi-player team setup with the losing team containing two players.
- [ ] Both losing-team players are eliminated by the scripted scenario.
- [ ] The scenario terminates with `OutcomeStatus.Decided`, `OutcomeReason.Eliminated`, and the expected `WinningTeam`.
- [ ] Winner/loser assertions fail if `WinningTeam` is unset or left at a default/sentinel value.
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

- Do not implement missing P1 team setup or P3 team-win logic here; block if prerequisites are incomplete.
- Do not reduce the scenario to a one-vs-one elimination; it must exercise team outcome reporting.
- Do not author the matrix-wide invariant audit; P7-07 owns that.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the relevant P7 checklist item(s) in [`implementation-checklist.md`](../../implementation-checklist.md) only if this card completes that item.
- Append a result line to repo-root `.exchange`.
- Confirm `team-win.json` and its focused test wiring were created.

## Result

<!-- append after completing -->
