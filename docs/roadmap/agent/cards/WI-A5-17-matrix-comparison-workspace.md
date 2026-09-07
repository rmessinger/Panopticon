# Task Card `WI-A5-17`: Add the matrix comparison workspace

| Field | Value |
|---|---|
| Card ID | `WI-A5-17` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-2 experiment matrix |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-05`, `WI-A5-16`, `WI-A5-18` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Load a matrix run set, filter cells, select a compatible pair, and show metric, assertion, and
divergence comparisons.

## Context to read (and only this)

- Matrix workspace flow in the A5 implementation plan.
- Matrix run-set reader from `WI-A5-16`.
- Results models from `WI-A5-04` and `WI-A5-18`.
- Comparator from `WI-A5-05`.
- ReplaySketch MAUI tab/view-model patterns.

## Steps

1. Add Core models for filtering, pair selection, and compatibility.
2. Compute metric deltas and assertion differences.
3. Invoke divergence comparison only for compatible recordings.
4. Bind the workspace to the Results tab.
5. Add headless selection/comparison tests and screenshot validation.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/`
- Narrow Results files under `src/OpenSage.Tools.ReplaySketch.Maui/`
- ReplaySketch tests

## Acceptance criteria

- [ ] Cells filter by axis and implementation binding.
- [ ] Any compatible pair can be selected.
- [ ] Incompatibility reports the exact field.
- [ ] Metric, assertion, and divergence links remain synchronized.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~MatrixComparison"
dotnet test src --filter Category!=Interactive
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-matrix.png dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No experiment execution or statistical redesign.

## On completion

- Set Status above to `Done` or `Blocked`.
- Tick A5-2 when all matrix cards are complete.
- Append a result line to `.exchange`.
