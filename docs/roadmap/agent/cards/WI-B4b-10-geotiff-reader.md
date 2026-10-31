# Task Card `WI-B4b-10`: GeoTIFF raster reader

| Field | Value |
|---|---|
| Card ID | `WI-B4b-10` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-3 local source readers |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-03` and `WI-B4b-09` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Implement `GeoTiffRasterReader : IGeospatialRasterReader` with in-house GeoTIFF key parsing over LibTiff.NET.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §3, §4 (supported CRS), §7 (Parse, Raster stages, limits).
- `test/Panopticon.Geospatial.Tests/Fixtures/SyntheticGeoTiffWriter.cs`.

## Steps

1. Open with LibTiff; check dimensions and decoded bytes against limits before allocating.
2. Read strips or tiles for `uint8`, `int16`, `uint16`, `int32`, and `float32` samples; reject other formats and compression LibTiff cannot decode with `Parse` errors.
3. Parse tags 33550 (pixel scale), 33922 (tiepoint), 34264 (transformation), 34735 (GeoKeyDirectory), 42113 (GDAL_NODATA) into `AffineTransform`, `CoordinateSystemId`, and no-data.
4. Map keys 1024, 1025, 2048, 3072, and 4096; for `RasterPixelIsPoint` (1025 = 2) shift the transform by half a pixel to area convention.
5. Unsupported or missing CRS yields a `Coordinates` diagnostic; rotation or shear terms are rejected.
6. Wrap LibTiff exceptions as typed diagnostics with the exception as detail.
7. Add `test/Panopticon.Geospatial.Tests/Readers/GeoTiffRasterReaderTests.cs` using every WI-B4b-09 fixture.

## Files likely to change

- `src/Panopticon.Geospatial/Readers/GeoTiffRasterReader.cs`
- `src/Panopticon.Geospatial/Readers/GeoKeyParser.cs`
- `test/Panopticon.Geospatial.Tests/Readers/`

## Acceptance criteria

- [ ] The `Plane` fixture returns samples and affine that reproduce the plane at control pixels.
- [ ] `SouthUp` yields a transform with positive row scale and `IsNorthUp` false.
- [ ] `PixelIsPoint` origin differs from the area fixture by exactly half a pixel.
- [ ] `Epsg27700` fails with the unsupported-CRS code.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GeoTiffRasterReader"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No resampling.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-18` when their other prerequisites are `Done`.
