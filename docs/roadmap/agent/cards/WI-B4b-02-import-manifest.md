# Task Card `WI-B4b-02`: Import manifest schema and loader

| Field | Value |
|---|---|
| Card ID | `WI-B4b-02` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-1 input contract |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-01` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Load and validate `GeospatialImportManifest` from JSON with strict schema, relative-path confinement, and typed diagnostics.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §3 (manifest fields), §4 (target CRS, no-data, edge, tolerance policies), §5.2 (material rules), §5.3 (feature mappings), §7 (Manifest stage).
- `src/Panopticon.Geospatial/GeoDiagnosticCodes.cs`, `src/Panopticon.Geospatial/GeospatialImportLimits.cs`.

## Steps

1. Add manifest records: `GeospatialImportManifest`, `ManifestSource` (id, kind `kml|kmz|geotiff`, relative path, role `extent|elevation|imagery|classification|vector`, declared vertical datum), `TargetCoordinateSystemSpec` (`utm` zone/hemisphere or `transverse-mercator` optional central meridian), `VerticalPolicy` (origin meters, exaggeration), `RasterPolicy` (no-data `fail|nearest-valid|constant`, radius, constant; edge policy; `imageryPixelsPerSample` default 10), `ToleranceSpec`, `MaterialRule` (classification value or vector attribute match, palette material), `FeatureMappingRule`, `maxScaleError` default 0.001, `defaultMaterial`.
2. Add `ManifestLoader.Load(path, diagnostics)` using `System.Text.Json` with `UnmappedMemberHandling.Disallow`, kebab-case enum converters, and size check against `GeospatialImportLimits` before reading.
3. Resolve source paths relative to the manifest directory; reject absolute paths, `..` escapes, and URLs with `Manifest` diagnostics.
4. Validate required fields: exactly one `extent` source, at least one `elevation` source, a `defaultMaterial`, positive `metersPerGameUnit`, and an operator vertical tolerance when any elevation datum is `unknown`.
5. Add `test/Panopticon.Geospatial.Tests/Manifest/ManifestLoaderTests.cs` with inline JSON fixtures for each rule.

## Files likely to change

- `src/Panopticon.Geospatial/Manifest/`
- `src/Panopticon.Geospatial/GeoDiagnosticCodes.cs`
- `test/Panopticon.Geospatial.Tests/Manifest/`

## Acceptance criteria

- [ ] A valid manifest round-trips to the expected records.
- [ ] Unknown JSON members, path escapes, and missing required fields each produce a distinct `Manifest` diagnostic code and a null result.
- [ ] Oversized manifests fail before deserialization.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ManifestLoader"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No source reading.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-18` when their other prerequisites are `Done`.
