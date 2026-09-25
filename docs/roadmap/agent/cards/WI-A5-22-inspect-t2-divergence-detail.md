# Task Card `WI-A5-22`: Inspect T2 detail from a divergence

| Field | Value |
|---|---|
| Card ID | `WI-A5-22` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-4 divergence localization |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-05`, `WI-A5-18`, `WI-A5-21` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Open the T2 window matching the first divergent T1 subtree and show changed entity/system
quantities with explicit missing-data diagnostics.

## Context to read (and only this)

- T1-to-T2 flow in the A5 implementation plan.
- Comparator result from `WI-A5-05`.
- T2 metadata/artifact from `WI-A5-21`.
- Results frame model/view from `WI-A5-18`.

## Steps

1. Add a Core detail model keyed by frame, stable subtree, entity, system, and quantity.
2. Match a divergence report to a compatible T2 window.
3. Compute before/current values and deltas.
4. Bind the detail pane to Results.
5. Add exact synthetic pair, missing, unarmed, and incompatible tests.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/`
- Results detail files under `src/Panopticon.Tools.ReplaySketch.Maui/`
- ReplaySketch tests

## Acceptance criteria

- [ ] The seeded fixture identifies exact frame, subtree, entity, and quantity.
- [ ] Missing/unarmed/incompatible T2 states have distinct diagnostics.
- [ ] Selecting a detail synchronizes the replay frame.
- [ ] Core owns matching and delta logic.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~DivergenceDetail"
dotnet test src --filter Category!=Interactive
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-t2.png dotnet run --project src/Panopticon.Tools.ReplaySketch.Maui
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not rerun either simulation to recover missing detail.

## On completion

- Set Status above to `Done` or `Blocked`.
- Tick A5-4 when `WI-A5-05` and this card are complete.
- Append a result line to `.exchange`.
