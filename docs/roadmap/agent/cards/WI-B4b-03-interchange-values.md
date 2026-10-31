# Task Card `WI-B4b-03`: Geospatial interchange values and interfaces

| Field | Value |
|---|---|
| Card ID | `WI-B4b-03` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-2 geospatial interchange |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-01` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Add the immutable CPU-only interchange values and reader/transform interfaces from §3.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §3 and §4 (`GeospatialMapTransform`), §10.3.

## Steps

1. Add `CoordinateSystemId` (record struct: EPSG code or custom TM with central meridian, scale, false easting/northing; `ToString` stable).
2. Add `GeoPoint` (lat, lon degrees), `Vector2d` (double x, y) if no suitable type exists in the project, and `AffineTransform` (6 doubles; `PixelToWorld`, `WorldToPixel`, `IsNorthUp`; rejects zero determinant).
3. Add `GeospatialRaster<T>` (width, height, band count, affine, CRS, optional no-data, `ReadOnlyMemory<T>` samples, row-major band-interleaved) with bounds-checked `Get(band, row, col)`.
4. Add geometry records `GeoPointGeometry`, `GeoLineString`, `GeoPolygon` (outer ring plus holes), `GeoMultiGeometry`, and `GeospatialFeature` (source id, stable feature id, geometry, `IReadOnlyDictionary<string,string>` attributes, CRS, optional altitude mode).
5. Add `SourceProvenance` (source id, relative path, SHA-256 hex, byte length) and `GeospatialMapTransform` (fields from §4).
6. Add interfaces `IVectorSourceReader`, `IGeospatialRasterReader`, `ICoordinateTransformer` with the §10.3 signatures.
7. Add `test/Panopticon.Geospatial.Tests/Interchange/AffineTransformTests.cs` and `GeospatialRasterTests.cs`.

## Files likely to change

- `src/Panopticon.Geospatial/Interchange/`
- `test/Panopticon.Geospatial.Tests/Interchange/`

## Acceptance criteria

- [ ] `AffineTransform.WorldToPixel(PixelToWorld(p))` returns `p` within 1e-9.
- [ ] `GeospatialRaster<T>` rejects sample buffers whose length does not match width x height x bands.
- [ ] No type references `MapFile`, LibTiff, or ImageSharp.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Panopticon.Geospatial.Tests.Interchange"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No implementations of the interfaces.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-05`, `WI-B4b-07`, `WI-B4b-09`, `WI-B4b-10`, and `WI-B4b-11` when their other prerequisites are `Done`.
