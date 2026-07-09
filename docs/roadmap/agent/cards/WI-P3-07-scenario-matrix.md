# Task Card: `WI-P3-07` — P3 scenario matrix

| Field | Value |
|---|---|
| Card ID | `WI-P3-07` |
| Roadmap phase | [Track P · P3](../../track-p-playable-baseline.md) — P3 scenario matrix, see [deep dive §6](../../deep-dives/p3-victory-defeat.md#6-scenario-matrix) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P3-01`, `WI-P3-02`, `WI-P3-03`, `WI-P3-04`, `WI-P3-05`, and `WI-P3-06` |
| Diagrams | [P3 Architecture Diagrams](../../deep-dives/p3-victory-defeat.md#7-architecture-diagrams) |

## Goal

Author and enable the P3 scenario matrix that sweeps end-state type by player count, pruning invalid combinations and asserting the correct `GameOutcome` for every valid cell.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p3-victory-defeat.md:154-180` — matrix axes, constraints, and per-cell assertions.
- `docs/roadmap/implementation-checklist.md:229-234` — scenario-matrix AC.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs:96-151` — matrix expansion, filters, and assertion templates.
- `src/OpenSage.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs:9-23` — runner over expanded cells.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:169-189` — outcome assertion surface to use.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:69-98` — existing P3 matrix stubs to replace/enable.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:226-240` — P7 dependency on P3 end-state assertions.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-70` — headless test base; matrix validation must run in CI.

## Steps

1. Replace the P3 stub tests with real, non-skipped tests. Prefer headless `[Fact]` tests that do not require a game installation; do not use a skipped test as the acceptance gate.
2. Build a `ScenarioMatrix` with axes: `endState` = `PlayerEliminated`, `PlayerSurrenders`, `AISurrenders`, `TeamWin`, `MutualElimination`, `ScoreDecision`; `playerCount` = `2`, `3`, `4`.
3. Add filters for invalid combinations from the deep dive: team-win configurations need at least two teams, mutual elimination requires at least two simultaneous losers, and score decision requires a configured frame limit.
4. For each valid cell, script the minimal scenario that reaches the target end state using the APIs from the completed P3 cards.
5. Attach outcome assertions for `GameOutcome.Status`, `GameOutcome.Reason`, winning/losing side, and bounded termination frame. `MutualElimination` cells must assert the sudden-death transition and the later decided result.
6. Ensure aggregate failures identify the matrix cell by axis bindings so a bad end state cannot pass silently.
7. New `.cs` files must be UTF-8 with BOM if you split the tests into a new file. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs`
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P3ScenarioMatrixTests.cs` (new, only if splitting P3 out of the shared stub file)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs` (only if matrix assertions need small model support)
- `src/OpenSage.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs` (only if runner reporting needs cell binding details)

## Acceptance criteria

- [ ] The matrix includes `endState` values `{PlayerEliminated, PlayerSurrenders, AISurrenders, TeamWin, MutualElimination, ScoreDecision}` and `playerCount` values `{2, 3, 4}`.
- [ ] Invalid combinations are pruned by `ConstraintFilter` or equivalent explicit filtering.
- [ ] One scripted scenario runs per valid cell.
- [ ] Every cell asserts `GameOutcome.Status`, `GameOutcome.Reason`, and winning/losing sides.
- [ ] Mutual-elimination cells assert `SuddenDeath` first and `Decided`/`Eliminated` after the next elimination.
- [ ] Every run terminates within a bounded frame count; infinite-loop failures are reported with the cell binding.
- [ ] Matrix tests are non-skipped headless `[Fact]` tests and run in CI without game files.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P3_ScenarioMatrixTests|FullyQualifiedName~P3ScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement missing P3 engine behavior in this card; if any prerequisite end state is not implemented, mark this card `Blocked`.
- Do not require game files or mark the matrix Interactive unless unavoidable; a skipped/Interactive-only matrix does not satisfy the gate.
- Keep scenarios minimal; do not author full P7 regression invariants here.
- Do not add new matrix axes beyond `endState × playerCount`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "P3 scenario matrix" item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` with the number of valid cells and tests run.
- Confirm the required matrix test file `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs` was updated, or create `src/OpenSage.Game.Tests/Tools/ReplaySketch/P3ScenarioMatrixTests.cs` if splitting the tests.

## Result

<!-- append after completing -->
