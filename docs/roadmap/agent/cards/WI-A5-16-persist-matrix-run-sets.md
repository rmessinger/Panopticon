# Task Card `WI-A5-16`: Persist keyed matrix run sets

| Field | Value |
|---|---|
| Card ID | `WI-A5-16` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-2 experiment matrix |
| Status | `Blocked` |
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

- [ ] A two-axis matrix writes exactly one bundle per cell.
- [ ] Keys are stable and collision-free for the supported value types.
- [ ] Failed and successful cells remain inspectable.
- [ ] Moving the complete run set preserves all references.
- [ ] Standard validation passes.

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
