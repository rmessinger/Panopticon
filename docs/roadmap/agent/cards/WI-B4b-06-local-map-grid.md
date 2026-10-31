# Task Card `WI-B4b-06`: Local map grid and distortion check

| Field | Value |
|---|---|
| Card ID | `WI-B4b-06` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-4 coordinate normalization |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-02`, `WI-B4b-05` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Build `LocalMapGrid` from the extent polygon: target CRS resolution, chunk-rounded rectangle, origin, and scale-error rejection.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §4 (import sequence, custom central meridian, `maxScaleError`) and §5.1a (chunk layout).
- `src/Panopticon.Geospatial/Manifest/GeospatialImportManifest.cs`, `src/Panopticon.Geospatial/Projection/`.

## Steps

1. Add `ChunkLayout` constants: lattice spacing 10 game units, 128 intervals per chunk, 129 points, apron 1, stored 131.
2. Add `LocalMapGrid.Create(extentPolygon, manifest, transformer, diagnostics)`: resolve the target CRS (custom TM central meridian defaults to the WGS84 bbox centre rounded to 0.01 degrees), project the polygon, build the axis-aligned rectangle rounded outward to whole chunks, and set the southwest origin.
3. Evaluate `|k/k0 - 1|` at the four extent corners and four edge midpoints; exceed `maxScaleError` produces a `Coordinates` error.
4. Expose `Transform` (`GeospatialMapTransform`), `ChunksX`, `ChunksY`, `SampleToProjected(x, y)`, `ProjectedToGame`, and the projected playable-area polygon.
5. Check chunk count against `GeospatialImportLimits` with checked arithmetic.
6. Add `test/Panopticon.Geospatial.Tests/Grid/LocalMapGridTests.cs`.

## Files likely to change

- `src/Panopticon.Geospatial/Grid/`
- `test/Panopticon.Geospatial.Tests/Grid/`

## Acceptance criteria

- [ ] A 1.28 km square extent at `metersPerGameUnit` 1 yields exactly 1 x 1 chunk.
- [ ] A polygon spanning 1.5-7.5 E with custom TM passes; the same polygon with `utm` zone 31 fails the scale check.
- [ ] `SampleToProjected(0, 0)` equals the origin.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~LocalMapGrid"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No raster sampling.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-12` and `WI-B4b-15` when their other prerequisites are `Done`.
