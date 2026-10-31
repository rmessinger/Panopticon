# Task Card `WI-B4b-09`: Synthetic GeoTIFF fixture writer

| Field | Value |
|---|---|
| Card ID | `WI-B4b-09` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-3 local source readers |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-B4b-03` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Add a test-only writer that produces small GeoTIFFs with known georeferencing so readers and generators never need downloaded data.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §8 (fixtures).
- GeoTIFF 1.1 (OGC 19-008r4) tags 33550, 33922, 34264, 34735, 42113.

## Steps

1. Add `test/Panopticon.Geospatial.Tests/Fixtures/SyntheticGeoTiffWriter.cs` using LibTiff.NET to write single-band `float32` or `int16` and 3/4-band `uint8` rasters with ModelPixelScale plus ModelTiepoint, or ModelTransformation, a GeoKeyDirectory (1024 model type, 1025 raster type, 2048 geographic CRS or 3072 projected CRS, 4096 vertical CRS), and GDAL_NODATA.
2. Add fixture builders: `Plane(slopeX, slopeY)`, `Ridge`, `WithNoDataHole`, `SouthUp` (positive row scale to expose mirroring), `Imagery(controlPixels)`, `Classification(values)`, `Epsg27700` (unsupported), and `PixelIsPoint`.
3. Write to a per-test temp directory cleaned up on dispose.
4. Add `test/Panopticon.Geospatial.Tests/Fixtures/SyntheticGeoTiffWriterTests.cs` verifying LibTiff can reopen each fixture and the tags are present.

## Files likely to change

- `test/Panopticon.Geospatial.Tests/Fixtures/`

## Acceptance criteria

- [ ] Every builder writes a file LibTiff reopens with the expected width, height, and tags.
- [ ] Fixtures are generated at test time; no binary TIFF is committed.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SyntheticGeoTiffWriter"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No production reader.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-10` when their other prerequisites are `Done`.
