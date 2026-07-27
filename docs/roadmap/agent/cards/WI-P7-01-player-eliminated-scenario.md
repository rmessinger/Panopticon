# Task Card `WI-P7-01`: PlayerEliminated regression scenario

| Field | Value |
|---|---|
| Card ID | `WI-P7-01` |
| Roadmap phase | [Track P · P7](../../track-p-playable-baseline.md): P7-1 PlayerEliminated, see [deep dive §6](../../deep-dives/p7-end-to-end-regression.md#6-sub-phases) and [§2](../../deep-dives/p7-end-to-end-regression.md#2-the-six-end-state-scenarios) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | Track P phases `P0`, `P2`, `P3`, `P4`, `P5`, `P6`, `P6b` complete; `WI-P3-02` and `WI-P3-03` done for elimination-to-outcome behavior |
| Diagrams | [P7 Architecture Diagrams](../../deep-dives/p7-end-to-end-regression.md#8-architecture-diagrams) |

## Goal

Create the checked-in P7 PlayerEliminated scenario that drives a human player's last command/production structures to destruction and proves the match terminates as `Decided/Eliminated` with the expected winner/loser.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:38-45`: expected P7 end-state outcomes.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:47-54`: deterministic scenario storage and scripting approach.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:58-70`: universal invariant set and per-tick failure behavior.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:111-111`: P7-1 sub-phase row.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:279-291`: Regression directory, JSON format, and pending P3 `GameOutcome` query path.
- `docs/roadmap/implementation-checklist.md:337-344`: six scenario files and expected `GameOutcome` ACs.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ReplayScenario.cs:6-22`: scenario JSON shape.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/PlayerSlotConfig.cs:6-24`: player faction/team/action slots.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ActionEntry.cs:7-20`: action entries and open command payloads.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandSpec.cs:86-96`: `CreateOpen` for arbitrary `OrderType` commands.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs:32-127`: run-and-assert entry point and frame timeout.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:169-189`: outcome assertion surface; replace placeholder `InProgress` behavior after P3.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:226-242`: final P7 matrix gate that will consume this scenario.

## Steps

1. Create `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/` if it still does not exist.
2. Add `player-eliminated.json` as a deterministic `ReplayScenario` with fixed seed/timing and the minimum player setup needed for one human player to lose all command/production structures with no rebuild path.
3. Script only P0 harness commands needed to reach the end state; prefer existing build/produce/attack command support and do not invent a new harness API.
4. Add an outcome assertion that reads the P3 `GameOutcome` path and expects `OutcomeStatus.Decided`, `OutcomeReason.Eliminated`, the correct `WinningPlayer`, and the eliminated human as losing player. Verify the assertion cannot pass against default/sentinel `InProgress` values.
5. Add or extend focused P7 scenario test wiring so this single scenario is loaded from the checked-in JSON, run with `HarnessRunner.RunAndAssert`, and fails the test process if the assertion report fails.
6. Attach any universal invariants already available in the shared P7 assertion set; P7-07 will audit and normalize all three invariants across all six scenarios.
7. If any new `.cs` file is created, ensure it is UTF-8 with BOM. Run `dotnet format src`, then verify `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/player-eliminated.json` (new)
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P7RegressionScenarioTests.cs` (new or updated focused test wiring)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` (only if the P3 `GameOutcome` path still needs to replace placeholders)

## Acceptance criteria

- [ ] `player-eliminated.json` exists under `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/` and is static checked-in scenario data, not generated during tests.
- [ ] The scenario terminates headlessly with `OutcomeStatus.Decided` and `OutcomeReason.Eliminated`.
- [ ] The expected `WinningPlayer` and losing human player are asserted; default/sentinel `GameOutcome` values cannot satisfy the assertion.
- [ ] The focused test exits non-zero on scenario or assertion failure; it is not a skipped `[GameFact]`/`[Fact(Skip=...)]` acceptance gate.
- [ ] Any applicable universal invariants run per tick and report the offending frame on failure.
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

- Do not implement missing P3 victory/defeat logic here; block if `WI-P3-02`/`WI-P3-03` are not done.
- Do not rely on live, nondeterministic AI to eliminate the player; use deterministic scripted orders.
- Do not weaken the assertion to `outcome.status == InProgress` or another default/sentinel value.
- Do not author the other five P7 scenarios in this card.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the relevant P7 checklist item(s) in [`implementation-checklist.md`](../../implementation-checklist.md) only if this card completes that item.
- Append a result line to repo-root `.exchange`.
- Confirm `player-eliminated.json` and its focused test wiring were created.

## Result

<!-- append after completing -->
