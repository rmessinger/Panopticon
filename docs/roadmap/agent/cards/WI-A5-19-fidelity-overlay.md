# Task Card `WI-A5-19`: Add the fidelity overlay and legend

| Field | Value |
|---|---|
| Card ID | `WI-A5-19` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-3 fidelity overlay |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-12`, `WI-A5-18` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Render an optional domain-supplied fidelity category without depending on B7 implementation types.

## Context to read (and only this)

- Fidelity visualization contract in the A5 implementation plan.
- Domain visual primitive types from `WI-A5-12`.
- Results renderer from `WI-A5-18`.
- `docs/roadmap/deep-dives/b7-variable-fidelity.md` §7.

## Steps

1. Add the optional categorical attribute and stable display metadata defined by A5.
2. Add overlay enablement, colors, and legend to the Results view.
3. Keep missing data as a disabled state with an explicit explanation.
4. Add synthetic Full/Reduced/Aggregated fixtures and screenshot validation.

## Files likely to change

- Domain visual primitive models
- Results renderer/view files
- ReplaySketch tests

## Acceptance criteria

- [ ] Synthetic fidelity values render distinct documented categories.
- [ ] Missing values disable the overlay without failure.
- [ ] A5 does not duplicate B7 policy or simulation types.
- [ ] Legend and frame state stay synchronized.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~FidelityOverlay"
dotnet test src --filter Category!=Interactive
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-fidelity.png dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

B7 supplies live fidelity values later; do not implement its algorithms here.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
