# Task Card: `WI-P0-14` — ScenarioMatrix model and runner (P0-5b)

| Field | Value |
|---|---|
| Card ID | `WI-P0-14` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-5b, see [deep dive §3.5b](../../deep-dives/playable-baseline-harness.md)) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-08` (HarnessRunner.RunAndAssert must exist) |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/playable-baseline-harness.md#9-architecture-diagrams) |

## Goal

Implement the `ScenarioMatrix` model (axes, constraint filters, assertion templates) and a
`ScenarioMatrixRunner` that expands the matrix into one headless run per cell via
`HarnessRunner.RunAndAssert` and produces an aggregate JSON report keyed by axis-binding.
This is the primitive the P1–P7 phase matrices depend on.

## Context to read (and only this)

- `docs/roadmap/deep-dives/playable-baseline-harness.md §3.5b` — full design: model types,
  expansion algorithm, output shape, and the aggregate JSON report format.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ReplayScenario.cs` — the template this matrix
  wraps per-cell.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` — `AssertionTemplate`
  will produce concrete assertions by substituting axis tokens.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionReport.cs` — per-cell report type;
  aggregate report is a new type that wraps a list of per-cell results.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs` — the entry point called per cell.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs` — the
  `P0_5b_ScenarioMatrixTests` stubs that this card must unblock.

## Steps

1. In `OpenSage.Tools.ReplaySketch.Core/Model/`, create `ScenarioMatrix.cs` containing:

   - `SweepAxis`: `string Name`, `IReadOnlyList<string> Values`.
   - `ConstraintFilter`: `string Expression` (a simple predicate string for documentation; for now
     implement the constraint as a `Func<IReadOnlyDictionary<string, string>, bool>` passed at
     construction time — no expression parser needed yet).
   - `AssertionTemplate`: `string Kind` (`"state"` / `"invariant"`), `string Id`, `string Path`,
     `string Op`, `string Value`. Each of these fields may contain `{axisName}` tokens substituted
     per-cell. `ToConcreteAssertion(IReadOnlyDictionary<string,string> bindings)` returns a
     concrete `ScenarioAssertion` with tokens replaced by their bound values.
   - `ScenarioMatrix`: `ReplayScenario BaseScenario`, `IReadOnlyList<SweepAxis> Axes`,
     `IReadOnlyList<ConstraintFilter> Filters`, `IReadOnlyList<AssertionTemplate> AssertionTemplates`.
     Method `IReadOnlyList<MatrixCell> Expand()` — cartesian product of all axis value lists,
     filtered, each cell bound to a `IReadOnlyDictionary<string,string>` of axis-name → value.
   - `MatrixCell`: `IReadOnlyDictionary<string,string> Bindings`,
     `ReplayScenario ConcreteScenario` (template with assertion templates resolved for this cell).

2. In `OpenSage.Tools.ReplaySketch.Core/`, create `ScenarioMatrixRunner.cs`:

   `ScenarioMatrixRunner.Run(ScenarioMatrix matrix, GameInstallation installation, string tempDirectory)`
   returns a `MatrixReport`.

   Algorithm:
   a. `matrix.Expand()` → list of cells.
   b. For each cell, call `HarnessRunner.RunAndAssert(cell.ConcreteScenario, installation, tempDir)`.
   c. Collect each `(cell.Bindings, AssertionReport)` as a `CellResult`.
   d. Return `MatrixReport(cells: List<CellResult>)` where
      `AllPassed = cells.All(c => c.Report.AllPassed)`.

3. Create `MatrixReport.cs` with:
   - `IReadOnlyList<CellResult> Cells`
   - `bool AllPassed` (computed)
   - `string ToJson()` — emits:
     ```json
     { "totalCells": N, "passed": P, "failed": F,
       "cells": [{ "key": {bindings}, "status": "passed"|"failed", "assertions": [...] }] }
     ```

4. Update the three `P0_5b_ScenarioMatrixTests` stubs in
   `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs`:

   a. `Matrix_Expansion_ProducesOneRunPerCell`:
      Build a `ScenarioMatrix` with 2 axes of lengths 2 and 3 (no `BaseScenario` needed —
      just expand). Assert `matrix.Expand().Count == 6`.

   b. `Matrix_AllCellsPass_ExitsZeroAndEmitsAggregateReport`:
      Build a `ScenarioMatrix` with one axis and one `AssertionTemplate` that will always pass
      (e.g., `player[0].money >= 0`). Assert `matrix.Expand().Count == 1` and verify
      `MatrixReport.AllPassed` shape without a live game (use a mock / check model only, or tag
      as Interactive if a live run is needed).

   c. `Matrix_OneCellFails_ExitsNonZeroAndReportsCell`:
      Build a `ScenarioMatrix` with two cells where one assertion token resolves to a known-failing
      value (e.g., `player[0].money > 999999`). Without a live game this can be a model test that
      verifies the aggregate report JSON contains the failing cell's key. Tag as Interactive if
      a live game is needed.

5. Add unit tests in `OpenSage.Tools.ReplaySketch.Tests/ScenarioMatrixTests.cs`:
   - `Expand_TwoAxes_ProducesCartesianProduct`: 2×3 axes → 6 cells.
   - `Expand_WithConstraintFilter_PrunesDisallowedCells`: constraint removes cells where
     axis `a == "x" && axis b == "y"`.
   - `AssertionTemplate_Resolve_SubstitutesTokens`: a template with `Path = "player[{idx}].money"`
     and binding `{"idx": "0"}` resolves to `Path = "player[0].money"`.
   - `MatrixReport_ToJson_EmitsCorrectShape`: build a 2-cell report (one pass, one fail) and
     assert JSON contains `"totalCells":2`, `"passed":1`, `"failed":1`, and both cell keys.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/MatrixReport.cs` (new)
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs` — unblock P0_5b stubs
- `src/OpenSage.Tools.ReplaySketch.Tests/ScenarioMatrixTests.cs` (new unit tests)

## Acceptance criteria

- [ ] `ScenarioMatrix.Expand()` with 2 axes of lengths 2 and 3 returns exactly 6 cells.
- [ ] A `ConstraintFilter` that rejects one combination reduces the count by 1.
- [ ] `AssertionTemplate.ToConcreteAssertion` substitutes `{axisName}` tokens correctly.
- [ ] `MatrixReport.ToJson()` contains `totalCells`, `passed`, `failed`, and a `cells` array
      with per-cell `key` (axis bindings) and `assertions` detail.
- [ ] The three `P0_5b_ScenarioMatrixTests` stubs in `P0HarnessAcceptanceTests.cs` no longer
      throw `NotImplementedException`.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src/OpenSage.Tools.ReplaySketch.Tests
dotnet test src --filter "FullyQualifiedName~P0_5b"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement the P1–P7 matrix axis values or scenarios — WI-P0-14 only provides the
  infrastructure.
- Do not add a parallel/concurrent cell runner — single-threaded sequential execution only.
- Do not implement a JSON expression parser for `ConstraintFilter` — accept a `Func<>` for now.

## On completion

- Set Status above to `Done`.
- Tick the P0-5b scenario-matrix item in the [implementation checklist](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
