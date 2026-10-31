# Task Card `WI-B4b-11`: Deterministic raster sampling kernels

| Field | Value |
|---|---|
| Card ID | `WI-B4b-11` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-5 terrain and imagery generation |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-02`, `WI-B4b-03` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Implement `RasterSampler` with nearest, bilinear, and area-weighted kernels plus no-data and edge policies.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §4 (no-data, edge policy, resampling) and §5.1 (determinism).
- `src/Panopticon.Geospatial/Interchange/GeospatialRaster.cs`.

## Steps

1. Add `SampleKernel` (Nearest, Bilinear, AreaWeighted) and `RasterSampler.Sample(raster, band, sourceX, sourceY, footprint, kernel)` taking source-CRS coordinates.
2. Select the kernel automatically from the ground-sample-distance ratio when not categorical (bilinear if output spacing <= source GSD, else area-weighted).
3. Implement no-data policies `fail`, `nearest-valid` (bounded radius, ties by lowest row then column), `constant`; implement edge policies separately; count fills and outside-footprint samples in a `SamplingStats`.
4. Use fixed iteration order and `double` accumulation; no parallelism inside one sample.
5. Add `test/Panopticon.Geospatial.Tests/Sampling/RasterSamplerTests.cs`.

## Files likely to change

- `src/Panopticon.Geospatial/Sampling/`
- `test/Panopticon.Geospatial.Tests/Sampling/`

## Acceptance criteria

- [ ] Bilinear sampling of a plane is exact within 1e-9.
- [ ] Area-weighted 4:1 decimation equals the block mean.
- [ ] `nearest-valid` tie-break picks the lowest row then column.
- [ ] Stats separate no-data fills from outside-footprint samples.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~RasterSampler"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No chunking.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-12` when their other prerequisites are `Done`.
