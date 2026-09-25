# Task Card `WI-A5-33`: Add non-spatial topology editing

| Field | Value |
|---|---|
| Card ID | `WI-A5-33` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-1 visual authoring |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-12`, `WI-A5-14` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Edit graph nodes, edges, and initial populations through the domain-neutral authoring surface while
leaving spatial map/terrain topology in WorldBuilder.

## Context to read (and only this)

- Topology-authoring ownership in the A5 implementation plan.
- Graph domain and projection from `WI-A4-09` and `WI-A5-12`.
- Shared population controller from `WI-A5-14`.
- Existing ReplaySketch Core renderer/controller patterns.

## Steps

1. Add a Core graph-topology edit controller using domain topology descriptors.
2. Support add/move/delete node and add/delete edge.
3. Route population edits through the shared controller.
4. Validate connectivity and stable identifiers before save.
5. Add graph round-trip tests and screenshot validation.

## Files likely to change

- ReplaySketch Core authoring files
- Narrow ReplaySketch MAUI files
- ReplaySketch tests

## Acceptance criteria

- [ ] Graph topology operations are headlessly tested.
- [ ] Stable IDs and connectivity validate before serialization.
- [ ] Population edits reuse `WI-A5-14`.
- [ ] No spatial terrain editor is duplicated.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~TopologyEditor"
dotnet test src --filter Category!=Interactive
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-topology.png dotnet run --project src/Panopticon.Tools.ReplaySketch.Maui
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No city/zone implementation beyond the generic contract and no WorldBuilder terrain changes.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
