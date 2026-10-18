# Task Card `WI-A5-16`: Persist keyed matrix run sets

| Field | Value |
|---|---|
| Card ID | `WI-A5-16` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-2 experiment matrix |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-39`, `WI-A5-09`, `WI-A5-15` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Write one portable run bundle per matrix cell plus an aggregate manifest keyed by stable axis
bindings.

## Context to read (and only this)

- Matrix run-set schema in the A5 implementation plan.
- `src/Panopticon.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs`.
- Run-bundle writer from `WI-A5-09`.
- A4 experiment runner from `WI-A4-39`.
- `Assertions/MatrixReport.cs`.

## Steps

1. Create stable cell-directory keys from normalized bindings.
2. Write each cell through the run-bundle writer.
3. Write the aggregate manifest with bindings, selected implementations, status, and relative path.
4. Preserve completed cells and a nonzero aggregate status when one cell fails.
5. Add two-axis, failure, relocation, and collision tests.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs`
- Matrix report/manifest types
- ReplaySketch tests

## Acceptance criteria

- [x] A two-axis matrix writes exactly one bundle per cell.
  - `MatrixRunSetSmokeTests.RunToBundles_WritesManifestAndKeyedCellBundles` (install-backed, 2 seed cells);
    engine-free `RunCells` orchestration covers the same layout.
- [x] Keys are stable and collision-free for the supported value types.
  - `BuildCellKey` ordinal-sorts axis=value pairs and sanitizes file-name characters; sanitized
    collisions (v/1 vs v_1) throw naming both raw bindings.
- [x] Failed and successful cells remain inspectable.
  - A throwing cell is recorded as failed with its error; other cells still run and their
    bundle directories persist (`FailureIsolation_RecordsErrorAndPreservesCompletedCells`).
- [x] Moving the complete run set preserves all references.
  - `runset.json` carries relative cell paths only; after `Directory.Move` the manifest is
    byte-identical and every cell directory resolves under the new root.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~MatrixRunSet"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No Results-tab implementation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
