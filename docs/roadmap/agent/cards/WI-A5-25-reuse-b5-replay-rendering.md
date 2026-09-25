# Task Card `WI-A5-25`: Reuse B5 LOD and instancing in replay

| Field | Value |
|---|---|
| Card ID | `WI-A5-25` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-5 visualization at scale |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-24`; B5 reusable batching, culling, and LOD contracts implemented |
| Diagrams | B5 diagrams and [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Feed replay visual primitives into B5's reusable instance batches and select bounded visual LODs by
screen-space size.

## Context to read (and only this)

- B5 implementation plan and reusable renderer contracts.
- Streaming/culling output from `WI-A5-24`.
- Results renderer from `WI-A5-18`.
- ReplaySketch MAUI renderer and screenshot instructions.

## Steps

1. Adapt visible primitives to B5 instance batches.
2. Select the documented replay LODs by screen-space threshold.
3. Preserve domain styling and selection overlays.
4. Add Core count assertions for culling, batches, and LOD selection.
5. Validate Results and Map screenshots.

## Files likely to change

- ReplaySketch Core visualization adapters
- Narrow MAUI renderer files
- ReplaySketch tests

## Acceptance criteria

- [ ] Repeated entities batch instead of producing one render submission each.
- [ ] At least two visual LODs are selected at documented thresholds.
- [ ] Culling and batch counts are headlessly asserted.
- [ ] Map-panel behavior remains unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ReplayLod|FullyQualifiedName~ReplayInstance"
dotnet test src --filter Category!=Interactive
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-lod.png dotnet run --project src/Panopticon.Tools.ReplaySketch.Maui
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-map.png REPLAYSKETCH_SCREENSHOT_TAB=map dotnet run --project src/Panopticon.Tools.ReplaySketch.Maui
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not duplicate or fork B5 renderer infrastructure.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
