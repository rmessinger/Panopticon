# Task Card `WI-B4b-12`: Terrain chunk generator

| Field | Value |
|---|---|
| Card ID | `WI-B4b-12` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-5 terrain and imagery generation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-06` and `WI-B4b-11` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Generate `float32` terrain chunks in the §5.1a layout by inverse-warping each lattice sample into the elevation raster.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §4 (elevation conversion, single inverse warp) and §5.1/§5.1a.
- `src/Panopticon.Geospatial/Grid/`, `src/Panopticon.Geospatial/Sampling/`.

## Steps

1. Add `TerrainChunk` (cx, cy, 131 x 131 `float[]`, min, max, SHA-256 of little-endian bytes).
2. Add `TerrainChunkGenerator.Generate(grid, elevation, policy, cx, cy)`: for each stored sample (including apron) compute projected position, transform to raster CRS, sample once, convert with `gameZ = (m - verticalOrigin) * exaggeration / metersPerGameUnit`.
3. Traverse chunks row-major from southwest; samples within a chunk row-major from south.
4. Aggregate `SamplingStats` into a per-import terrain report record.
5. Add `test/Panopticon.Geospatial.Tests/Generation/TerrainChunkGeneratorTests.cs`.

## Files likely to change

- `src/Panopticon.Geospatial/Generation/TerrainChunk*.cs`
- `test/Panopticon.Geospatial.Tests/Generation/`

## Acceptance criteria

- [ ] Shared edge lattice points of adjacent chunks are bit-identical.
- [ ] The `SouthUp` fixture produces the same heights as the north-up fixture (no mirroring).
- [ ] Two runs produce identical chunk hashes.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~TerrainChunkGenerator"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No imagery, materials, or payload writing.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-13`, `WI-B4b-14`, `WI-B4b-16`, and `WI-B4b-17` when their other prerequisites are `Done`.
