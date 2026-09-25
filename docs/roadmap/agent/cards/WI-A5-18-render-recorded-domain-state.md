# Task Card `WI-A5-18`: Render recorded domain state in Results

| Field | Value |
|---|---|
| Card ID | `WI-A5-18` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-3 results replayer |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-04`, `WI-A5-08`, `WI-A5-12` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Render reconstructed domain primitives at the selected frame without rerunning the simulation, and
keep metrics and events synchronized.

## Context to read (and only this)

- Results-view design in the A5 implementation plan.
- Timeline/results models from `WI-A5-04`.
- State cursor from `WI-A5-08`.
- Domain visualization projection from `WI-A5-12`.
- `Profiles/MapPreviewRenderer.cs` and existing MAUI custom-draw patterns.

## Steps

1. Add a Core frame view model and domain-neutral overview renderer.
2. Project the selected state through the domain adapter.
3. Render owner/category styling and topology appropriate to RTS and graph fixtures.
4. Synchronize metric cursors and event selection.
5. Test empty, added, removed, moved, and non-spatial entities; validate screenshots.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/`
- Results view files under `src/Panopticon.Tools.ReplaySketch.Maui/`
- ReplaySketch tests

## Acceptance criteria

- [ ] Scrubbing changes rendered state without host execution.
- [ ] RTS and graph fixtures render through one view contract.
- [ ] Metrics and events track the selected frame.
- [ ] Core owns projection and hit-testing logic.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~RunFrame|FullyQualifiedName~ResultsRenderer"
dotnet test src --filter Category!=Interactive
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-results-state.png dotnet run --project src/Panopticon.Tools.ReplaySketch.Maui
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-map.png REPLAYSKETCH_SCREENSHOT_TAB=map dotnet run --project src/Panopticon.Tools.ReplaySketch.Maui
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No 3-D playback, streaming optimization, or fidelity algorithm.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
