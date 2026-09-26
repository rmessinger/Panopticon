# Task Card `WI-B4b-18`: Import pipeline and standalone payload

| Field | Value |
|---|---|
| Card ID | `WI-B4b-18` |
| Roadmap phase | [Track B · B4b](../../track-b-scale.md): B4b-6a standalone payload |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-02`, `WI-B4b-08`, `WI-B4b-10`, `WI-B4b-13`, `WI-B4b-14`, `WI-B4b-16`, and `WI-B4b-17` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Wire `GeospatialImporter.Run` end to end and write/read the §6.1 payload directory transactionally.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §6.1 (payload layout), §4 (vertical datum rule), §7, §10.4.

## Steps

1. Add `GeospatialImporter.Run(manifestPath, outputDirectory, cancellationToken)` returning `GeoImportResult` (success, diagnostics, payload path).
2. Order: load manifest; hash sources (SHA-256); read vector and raster sources; reject mixed known vertical datums; build grid; per chunk generate terrain, imagery, materials, masks; map features; write payload.
3. Add `GeneratedMapPayloadWriter` (writes to `<out>.tmp-<guid>`, then `Directory.Move`; deletes the temp directory on error or cancellation) and `GeneratedMapPayloadReader`.
4. Add `payload.json` schema (payload version 1, transform, chunk layout, palette, features, provenance) and `report.json` (diagnostics, kernels and GSD ratios, fill counts).
5. Check the cancellation token between chunks.
6. Add `test/Panopticon.Geospatial.Tests/Pipeline/GeospatialImporterTests.cs` with a synthetic end-to-end manifest (KML extent plus `Plane` DEM plus imagery plus classification).

## Files likely to change

- `src/Panopticon.Geospatial/Pipeline/`
- `src/Panopticon.Geospatial/Payload/`
- `test/Panopticon.Geospatial.Tests/Pipeline/`

## Acceptance criteria

- [ ] The synthetic import writes every file listed in §6.1 and the reader round-trips it.
- [ ] A mid-import cancellation or blocking diagnostic leaves no output or temp directory.
- [ ] Sources declaring `EGM96` and `EGM2008` fail with a `Coordinates` error.
- [ ] Two runs produce identical `payload.json` and chunk hashes.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GeospatialImporter"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No CLI, no MapFile.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-19`, `WI-B4b-20`, and `WI-B4b-23` when their other prerequisites are `Done`.
