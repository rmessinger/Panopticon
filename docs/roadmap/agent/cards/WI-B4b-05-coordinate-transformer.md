# Task Card `WI-B4b-05`: Coordinate transformer for supported CRS

| Field | Value |
|---|---|
| Card ID | `WI-B4b-05` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-4 coordinate normalization |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-03` and `WI-B4b-04` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Implement `ICoordinateTransformer` for EPSG:4326, WGS84 UTM, and custom Transverse Mercator.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §4 (supported coordinate systems).
- `src/Panopticon.Geospatial/Interchange/CoordinateSystemId.cs`, `src/Panopticon.Geospatial/Projection/TransverseMercatorProjection.cs`.

## Steps

1. Add `WgsTransverseMercatorTransformer : ICoordinateTransformer` supporting EPSG:4326, EPSG:32601-32660, EPSG:32701-32760, and custom TM ids.
2. Transform via geographic lat/lon as the pivot; cache one projection instance per CRS.
3. Add `IsSupported(CoordinateSystemId)`; unsupported ids produce a `Coordinates` diagnostic from a `TryTransform` overload and throw from `Transform`.
4. Add `test/Panopticon.Geospatial.Tests/Projection/CoordinateTransformerTests.cs`.

## Files likely to change

- `src/Panopticon.Geospatial/Projection/WgsTransverseMercatorTransformer.cs`
- `src/Panopticon.Geospatial/GeoDiagnosticCodes.cs`
- `test/Panopticon.Geospatial.Tests/Projection/`

## Acceptance criteria

- [ ] UTM 31N to UTM 32N via 4326 round-trips within 1 mm for a point near 6 E.
- [ ] EPSG:27700 is rejected with the unsupported-CRS diagnostic code.
- [ ] Custom TM with central meridian 3.5 matches `TransverseMercatorProjection` directly.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~CoordinateTransformer"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No grid or extent logic.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-06`.
