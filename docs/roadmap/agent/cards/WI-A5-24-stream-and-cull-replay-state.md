# Task Card `WI-A5-24`: Stream and cull replay state

| Field | Value |
|---|---|
| Card ID | `WI-A5-24` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-5 visualization at scale |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A5-18`, `WI-A5-29` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Decode and project only buffered frames and visible topology regions through the region-indexed
artifact so memory and draw-primitive counts stay bounded.

## Context to read (and only this)

- Streaming/culling contract in the A5 implementation plan.
- Region-indexed reader/cursor from `WI-A5-29`.
- Domain overview renderer from `WI-A5-18`.
- Domain topology and visual primitive contracts from `WI-A5-12`.

## Steps

1. Add viewport-to-topology visibility queries.
2. Request only visible region data and the configured adjacent prefetch window.
3. Bound decoded frame, entity, and primitive caches.
4. Invalidate caches correctly on pan, zoom, seek, and domain change.
5. Add synthetic long-run and large-world count/memory tests.

## Files likely to change

- Run cursor and visualization files in ReplaySketch Core
- ReplaySketch tests

## Acceptance criteria

- [ ] Off-screen entities produce no draw primitives.
- [ ] Pan/zoom/seek update the visible set correctly.
- [ ] Buffered frames and decoded entities remain bounded independently of run length.
- [ ] Spatial and graph fixtures use the same visibility API.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ReplayStreaming|FullyQualifiedName~Visibility"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No GPU instancing or B5 LOD implementation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
