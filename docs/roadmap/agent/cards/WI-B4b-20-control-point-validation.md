# Task Card `WI-B4b-20`: Control-point validation

| Field | Value |
|---|---|
| Card ID | `WI-B4b-20` |
| Roadmap phase | [Track R · R2](../../track-r-real-world-terrain.md): B4b-6a standalone payload |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-18` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Validate named control points against the generated payload within manifest tolerances and record results in `report.json`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §4 (tolerances) and §8 (operator proof).

## Steps

1. Add manifest `controlPoints` (name, lat, lon, optional expected elevation meters, optional expected imagery RGB).
2. Add `ControlPointValidator`: transform to game XY, sample payload terrain (and imagery), compare with horizontal and vertical tolerances; mark absolute vertical `not evaluated` when datum is `unknown`.
3. Run it at the end of `GeospatialImporter.Run`; failures are `Validate` errors.
4. Add `test/Panopticon.Geospatial.Tests/Pipeline/ControlPointValidatorTests.cs`.

## Files likely to change

- `src/Panopticon.Geospatial/Validation/`
- `src/Panopticon.Geospatial/Manifest/`
- `src/Panopticon.Geospatial/Pipeline/GeospatialImporter.cs`
- `test/Panopticon.Geospatial.Tests/Pipeline/`

## Acceptance criteria

- [ ] A control point on the synthetic plane passes; one displaced beyond tolerance fails with a `Validate` error.
- [ ] `unknown` datum marks absolute validation not evaluated.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ControlPointValidator"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No real-world data.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-24` when their other prerequisites are `Done`.
