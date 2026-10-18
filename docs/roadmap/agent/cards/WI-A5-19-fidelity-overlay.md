# Task Card `WI-A5-19`: Add the fidelity overlay and legend

| Field | Value |
|---|---|
| Card ID | `WI-A5-19` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-3 fidelity overlay |
| Status | `Done` |
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

- [x] Synthetic fidelity values render distinct documented categories.
  - `FidelityOverlayTests.DistinctCategories_RenderDistinctColorsAndCarryMarkers`: Full/Reduced/
    Aggregated map to distinct palette slots and markers carry the category.
- [x] Missing values disable the overlay without failure.
  - `OverlayWithoutCategories_DisablesGracefully`: explicit `fidelity data unavailable in this
    run` label, owner colors retained, no throw.
- [x] A5 does not duplicate B7 policy or simulation types.
  - Overlay consumes the free-form `DomainVisualPoint.FidelityCategory` string; the palette's
    first three slots are documented as covering B7's planned vocabulary without referencing
    B7 types.
- [x] Legend and frame state stay synchronized.
  - The legend is rebuilt inside every `RunFrameRenderer.Render` call from that frame's
    primitives; `ViewModel_ModeSwitch_RaisesFrameProjection` covers the repaint path.
- [x] Standard validation passes.
  - `FidelityOverlay` filter 5/5; results-tab screenshot captured under `DISPLAY=:99`.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~FidelityOverlay"
dotnet test src --filter Category!=Interactive
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-fidelity.png dotnet run --project src/Panopticon.Tools.ReplaySketch.Avalonia
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

B7 supplies live fidelity values later; do not implement its algorithms here.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
