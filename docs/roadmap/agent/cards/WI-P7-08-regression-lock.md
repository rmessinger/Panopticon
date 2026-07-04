# Task Card: `WI-P7-08` — P7 regression lock

| Field | Value |
|---|---|
| Card ID | `WI-P7-08` |
| Roadmap phase | [Track P · P7](../../track-p-playable-baseline.md) — P7-8 Regression lock, see [deep dive §6](../../deep-dives/end-to-end-regression.md#6-sub-phases) and [§5](../../deep-dives/end-to-end-regression.md#5-regression-locking) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | Track P phases `P0`, `P2`, `P3`, `P4`, `P5`, `P6`, `P6b` complete; `WI-P7-07` done |
| Diagrams | [P7 Architecture Diagrams](../../deep-dives/end-to-end-regression.md#8-architecture-diagrams) |

## Goal

Lock P7 as the standing playability regression suite by ensuring the six end-state scenario files are source-controlled inputs, unblocking `P7_ScenarioMatrixTests`, preserving test discovery, promoting the P6b archetype-contract scenarios into the same standing gate, and documenting the CI command.

## Context to read (and only this)

- `docs/roadmap/deep-dives/end-to-end-regression.md:91-103` — regression locking requirements.
- `docs/roadmap/deep-dives/end-to-end-regression.md:117-118` — P7-7/P7-8 sub-phase rows.
- `docs/roadmap/deep-dives/end-to-end-regression.md:210-240` — suite execution sequence diagram.
- `docs/roadmap/deep-dives/end-to-end-regression.md:284-287` — current `P7_ScenarioMatrixTests` methods to unblock.
- `docs/roadmap/implementation-checklist.md:345-360` — P6b promotion, CI/standing regression, and matrix ACs.
- `docs/roadmap/agent/README.md:66-75` — standard validation commands.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:222-254` — current P7 skipped stubs and method names.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/MatrixReport.cs:19-55` — aggregate report fields used by the tests.
- `src/OpenSage.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs:9-23` — canonical matrix runner.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs:32-127` — canonical harness run-and-assert path.
- `.editorconfig:14-14` — new `.cs` files must be UTF-8 with BOM.

## Steps

1. Verify all six scenario files exist under `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/` as source-controlled static inputs and are consumed by the P7 matrix from `WI-P7-07`.
2. Replace the `P7_ScenarioMatrixTests` stubs in `TrackPScenarioMatrixTests.cs` with real tests while preserving the class name, method names, and useful traits so test discovery continues to find the same P7 gate.
3. Remove the skip-only acceptance path. Prefer headless `[Fact]` tests that run under `dotnet test src --filter Category!=Interactive`; if a game install is truly unavoidable, document the exact runnable command and make the default test fail loudly with a clear blocked reason rather than silently passing/skipping.
4. Preserve any existing guard semantics that protect unsupported environments, but do not preserve a guard by deleting discovery or converting failures into success.
5. Wire `Matrix_AllEndStateCells_PassWithInvariants` to assert the six-cell aggregate report passes and includes all end-state keys.
6. Wire `Matrix_InvariantViolation_AbortsRunAndReportsCell` to prove a deliberate invariant violation aborts the cell, reports the frame, and fails the aggregate.
7. Promote the six P6b archetype-contract scenarios into the standing P7 regression command, or preserve their existing matrix command and document it as part of the P7 gate.
8. Add a README/agent-guide note for the CI filter command that runs the P7 suite as the standing playability check.
9. Verify default/sentinel correctness: an unset `GameOutcome` and an unset/default `MatchConfig.FrameLimit` must not accidentally satisfy the locked matrix assertions.
10. Ensure any new `.cs` files are UTF-8 with BOM. Run `dotnet format src`, then verify `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs`
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P7RegressionMatrixTests.cs` (only if `WI-P7-07` created helper coverage that must be folded into the canonical test class)
- `docs/roadmap/agent/README.md` (or the nearest existing roadmap README that documents scenario-matrix validation)
- `docs/roadmap/implementation-checklist.md`

## Acceptance criteria

- [ ] `P7_ScenarioMatrixTests.Matrix_AllEndStateCells_PassWithInvariants` is a real discovered test, not a skipped stub, and asserts all six cells pass.
- [ ] `P7_ScenarioMatrixTests.Matrix_InvariantViolation_AbortsRunAndReportsCell` is a real discovered test, not a skipped stub, and asserts cell key plus fail frame reporting.
- [ ] Existing P7 test discovery is preserved: class/method names remain recognizable and useful traits are not dropped without replacement.
- [ ] The suite actually runs in CI under `dotnet test src --filter Category!=Interactive` where possible; otherwise the exact unavoidable command is documented and failures are real failures, not silent skips.
- [ ] The standing P7 gate includes the six P6b archetype-contract scenarios or documents their exact companion command, and any open P6b gap blocks P7 rather than being ignored.
- [ ] The README/agent-guide documents the P7 regression command and explains it is the standing "is the game still playable?" check.
- [ ] Default/sentinel `GameOutcome` and frame-limit values cannot make the locked tests pass accidentally.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P7_ScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change P1-P6 matrix tests except for mechanically avoiding duplicate helper names if unavoidable.
- Do not regenerate or rewrite the six scenario files except for tiny lock-related metadata needed by the existing matrix.
- Do not replace the canonical `P7_ScenarioMatrixTests` with a differently named suite that CI will not discover.
- Do not mark this card done while the P7 gate is skipped or only documented as future work.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P7 regression-lock and scenario-matrix items in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` with the exact command run and result.
- Confirm the canonical P7 test wiring and CI/README note were created.

## Result

<!-- append after completing -->
