# Task Card `WI-P7-07`: Universal invariants and P7 scenario matrix

| Field | Value |
|---|---|
| Card ID | `WI-P7-07` |
| Roadmap phase | [Track P · P7](../../track-p-playable-baseline.md): P7-7 Universal invariants + matrix run, see [deep dive §6](../../deep-dives/p7-end-to-end-regression.md#6-sub-phases), [§3](../../deep-dives/p7-end-to-end-regression.md#3-universal-invariants), and [§4](../../deep-dives/p7-end-to-end-regression.md#4-scenario-matrix) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | Track P phases `P0`, `P2`, `P3`, `P4`, `P5`, `P6`, `P6b` complete; `WI-P7-01`, `WI-P7-02`, `WI-P7-03`, `WI-P7-04`, `WI-P7-05`, and `WI-P7-06` done |
| Diagrams | [P7 Architecture Diagrams](../../deep-dives/p7-end-to-end-regression.md#8-architecture-diagrams) |

## Goal

Attach the three universal invariants to all six P7 scenarios and prove the P0-5b `ScenarioMatrix` expands/runs the six end-state cells with a failing aggregate report on any invariant or outcome failure.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:58-70`: the three universal invariants and per-tick abort requirement.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:74-87`: P7 matrix axis and six-cell aggregate report.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:117-117`: P7-7 sub-phase row.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:210-240`: suite execution sequence diagram.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:293-296`: per-tick invariant hook via `Game.Updating`.
- `docs/roadmap/implementation-checklist.md:348-360`: invariant and matrix ACs.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs:96-151`: matrix expansion and assertion-template materialization.
- `src/OpenSage.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs:9-23`: P0-5b runner over expanded cells.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionEvaluator.cs:23-73`: per-tick invariant subscription and abort.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:135-167`: `InvariantAssertion` implementation.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:192-218`: `CodeAssertion` escape hatch for invariants that need code.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/CodeAssertionRegistry.cs:7-15`: predicate registration.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/MatrixReport.cs:19-55`: aggregate cell report and JSON shape.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:226-254`: final P7 matrix test names to satisfy in P7-08.

## Steps

1. Inspect all six P7 scenario JSON files and ensure each has the shared invariant set attached: money never negative, no unit duplication, and AI progress where AI participates.
2. Implement missing invariant assertion paths or code predicates narrowly. Unit duplication must be checked every tick by comparing owned-unit count to distinct owned-unit ids; do not reduce it to an end-of-match check.
3. Ensure invariant failure aborts the current run immediately and records the offending frame in the per-cell report.
4. Build a P7 `ScenarioMatrix` with exactly one axis, `endState`, whose values are `PlayerEliminated`, `PlayerSurrenders`, `AISurrenders`, `TeamWin`, `MutualElimination`, and `ScoreDecision`.
5. Wire the matrix cells to the six checked-in scenario files and run them through `ScenarioMatrixRunner.Run`, not a bespoke runner.
6. Add a deliberate invariant-violation coverage path that proves a failing invariant marks the matrix failed, includes the `endState` cell key, and returns a real test failure if not handled.
7. Tests must actually run in CI and fail loudly where possible. Prefer headless `[Fact]`; if a game install is unavoidable, document the exact runnable command and do not count a skipped test as the acceptance gate.
8. If any new `.cs` file is created, ensure it is UTF-8 with BOM. Run `dotnet format src`, then verify `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/player-eliminated.json`
- `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/player-surrenders.json`
- `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/ai-surrenders.json`
- `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/team-win.json`
- `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/mutual-elimination.json`
- `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/score-decision.json`
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P7RegressionMatrixTests.cs` (new focused matrix coverage, or equivalent helper consumed by P7-08)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` (only if invariant paths need real values)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/CodeAssertionRegistry.cs` (only if code predicates are needed)

## Acceptance criteria

- [ ] All six P7 scenarios carry the three universal invariants: money never negative, no unit duplication, and AI progress where applicable.
- [ ] Invariants run every tick through the `Game.Updating`/P0 assertion lifecycle and abort on first failure with the offending frame recorded.
- [ ] The P7 matrix has exactly six cells over the `endState` axis.
- [ ] The matrix aggregate report identifies each cell by `endState` and includes assertion details for failures.
- [ ] All six cells pass their end-state outcome assertions plus universal invariants.
- [ ] A deliberate invariant violation fails loudly and reports the correct cell and frame.
- [ ] The matrix coverage is not accepted as a skipped `[GameFact]`/`[Fact(Skip=...)]`; it runs headlessly in CI where possible or documents an exact unavoidable game-install command.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P7RegressionMatrixTests|FullyQualifiedName~P7_ScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not author new end-state scenarios here; block if any of `WI-P7-01` through `WI-P7-06` is missing.
- Do not bypass `ScenarioMatrixRunner` with a one-off loop.
- Do not weaken unit-duplication or AI-progress invariants into end-only assertions.
- Do not leave the matrix as a skipped stub that validates nothing.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the invariant and P7 scenario-matrix checklist items in [`implementation-checklist.md`](../../implementation-checklist.md) if fully satisfied.
- Append a result line to repo-root `.exchange` with the six-cell matrix result.
- Confirm the six scenario files and matrix test/helper were created or updated.

## Result

<!-- append after completing -->
