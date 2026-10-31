# Task Card `WI-B4b-15`: Feature transform and clipping

| Field | Value |
|---|---|
| Card ID | `WI-B4b-15` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-6 vector semantics |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-06` and `WI-B4b-07` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Transform KML features into local game coordinates and clip them to the map rectangle.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.3.
- `src/Panopticon.Geospatial/Grid/LocalMapGrid.cs`.

## Steps

1. Add `FeatureClipper.Clip(features, grid)`: transform each coordinate to projected then game XY, then clip lines with Liang-Barsky and polygons against the map rectangle. Preserve polygon holes and disconnected results as required by the §5.3 contract; independent ring clipping with Sutherland-Hodgman alone is insufficient.
2. Drop features fully outside with an `Info` diagnostic; keep stable ids and attributes; split multi-part results deterministically.
3. Add `LocalFeature` records (game-space geometry plus source feature).
4. Add `test/Panopticon.Geospatial.Tests/Features/FeatureClipperTests.cs`.

## Files likely to change

- `src/Panopticon.Geospatial/Features/`
- `test/Panopticon.Geospatial.Tests/Features/`

## Acceptance criteria

- [ ] A line crossing the rectangle is clipped to the exact boundary intersection.
- [ ] A polygon straddling a corner produces the expected clipped ring.
- [ ] A concave polygon clipped into disconnected pieces retains separate parts with stable IDs; holes retain their exclusion semantics.
- [ ] Points outside are dropped with a diagnostic.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~FeatureClipper"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No mapping rules.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-14` and `WI-B4b-16` when their other prerequisites are `Done`.
