# Task Card `WI-B4b-04`: WGS84 Transverse Mercator projection

| Field | Value |
|---|---|
| Card ID | `WI-B4b-04` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-4 coordinate normalization |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-01`, `WI-B4b-03` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Implement the Krüger-series (order 6) WGS84 Transverse Mercator forward, inverse, and point scale factor.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §4 (target CRS, scale-error rule).
- Karney, C. F. F. (2011), "Transverse Mercator with an accuracy of a few nanometers", J. Geodesy 85, eqs. 35-36 (series coefficients to order 6).

## Steps

1. Add `TransverseMercatorProjection` (constructor: central meridian degrees, scale k0, false easting, false northing; WGS84 a = 6378137, f = 1/298.257223563).
2. Implement `Forward(lat, lon)` returning easting/northing, `Inverse(e, n)` returning `GeoPoint`, and `ScaleFactor(lat, lon)` returning point scale k, using the order-6 alpha/beta series.
3. Add `TransverseMercatorProjection.Utm(zone, north)` preset (k0 0.9996, FE 500000, FN 0 or 10000000).
4. Add `test/Panopticon.Geospatial.Tests/Projection/TransverseMercatorTests.cs`.

## Files likely to change

- `src/Panopticon.Geospatial/Projection/TransverseMercatorProjection.cs`
- `test/Panopticon.Geospatial.Tests/Projection/`

## Acceptance criteria

- [x] Forward then inverse round-trips within 1 mm over lon ±5 degrees from the central meridian and lat -80..84 (worst 3.2e-9 m).
- [x] Northing of (45 N, central meridian) with k0 1 and FN 0 is 4984944.378 m within 1 mm.
- [x] Eiffel Tower (48.85826 N, 2.29451 E) in UTM 31N is within 5 m of E 448252.59, N 5411939.34 (GeographicLib GeoConvert 2.5.2 anchor).
- [x] `ScaleFactor` on the central meridian equals k0 within 1e-12 (worst 3.3e-16).
- [x] Standard validation passes: 20/20 projection facts, 195/195 geospatial tests with `Category!=Interactive`, `dotnet format` and brace check clean.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~TransverseMercator"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No CRS dispatch or grid logic.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-05` when their other prerequisites are `Done`.
