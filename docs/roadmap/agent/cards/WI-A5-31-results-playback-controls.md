# Task Card `WI-A5-31`: Add replay playback and event navigation

| Field | Value |
|---|---|
| Card ID | `WI-A5-31` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-3 results replayer |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-04`, `WI-A5-18` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Add play, pause, fast-forward, step, and event-to-frame navigation over recorded state.

## Context to read (and only this)

- Playback contract in the A5 implementation plan.
- Timeline/results model from `WI-A5-04`.
- Recorded-state renderer from `WI-A5-18`.
- Existing ReplaySketch Core controller and MAUI command patterns.

## Steps

1. Add a Core playback controller with explicit clock and speed options.
2. Implement play, pause, frame step, and bounded fast-forward.
3. Route event selection to the event frame.
4. Stop at artifact bounds and expose truncated-prefix diagnostics.
5. Add controller tests and Results/Map screenshots.

## Files likely to change

- ReplaySketch Core results controller
- Narrow Results view/view-model files
- ReplaySketch tests

## Acceptance criteria

- [ ] Every playback command changes state exactly as documented.
- [ ] Event selection seeks to the matching frame.
- [ ] Playback never reruns simulation.
- [ ] Bounds and truncated artifacts stop explicitly.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ResultsPlayback"
dotnet test src --filter Category!=Interactive
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-playback.png dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-map.png REPLAYSKETCH_SCREENSHOT_TAB=map dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No interpolation or 3-D playback.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
