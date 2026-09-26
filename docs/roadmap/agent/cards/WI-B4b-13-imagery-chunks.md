# Task Card `WI-B4b-13`: Imagery chunk generator

| Field | Value |
|---|---|
| Card ID | `WI-B4b-13` |
| Roadmap phase | [Track B · B4b](../../track-b-scale.md): B4b-5 terrain and imagery generation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-12` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Resample imagery rasters into chunk-aligned RGBA8 PNGs at `imageryPixelsPerSample`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.2.
- `src/Panopticon.Geospatial/Generation/TerrainChunkGenerator.cs` (traversal pattern).

## Steps

1. Add `ImageryChunkGenerator.Generate(grid, imagery, policy, cx, cy)` producing a `(128 * pps)` square RGBA8 buffer, north row first in the PNG.
2. Accept 3-band (alpha 255) and 4-band `uint8` rasters; reject other layouts with a `Raster` error.
3. Encode with `SixLabors.ImageSharp` PNG encoder using fixed settings; hash the encoded bytes.
4. Add `test/Panopticon.Geospatial.Tests/Generation/ImageryChunkGeneratorTests.cs` using the `Imagery(controlPixels)` fixture.

## Files likely to change

- `src/Panopticon.Geospatial/Generation/ImageryChunk*.cs`
- `src/Panopticon.Geospatial/Panopticon.Geospatial.csproj`
- `test/Panopticon.Geospatial.Tests/Generation/`

## Acceptance criteria

- [ ] Control pixels match the source after declared resampling within 1 per channel.
- [ ] Encoded bytes are identical across two runs.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ImageryChunkGenerator"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No in-game display.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-18` when their other prerequisites are `Done`.
