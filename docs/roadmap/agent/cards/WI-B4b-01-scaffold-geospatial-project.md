# Task Card `WI-B4b-01`: Scaffold `Panopticon.Geospatial`, diagnostics, and limits

| Field | Value |
|---|---|
| Card ID | `WI-B4b-01` |
| Roadmap phase | [Track B · B4b](../../track-b-scale.md): B4b-1 input contract |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Create the CPU-only `Panopticon.Geospatial` project and test project with the diagnostic and resource-limit types every later B4b card uses.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §3 (dependencies) and §7 (diagnostics, limits).
- `src/Panopticon.Mathematics/Panopticon.Mathematics.csproj`: minimal project with no engine references.
- `test/Panopticon.Mathematics.Tests/Panopticon.Mathematics.Tests.csproj`: test project pattern.
- `Directory.Packages.props`: central package versions.

## Steps

1. Create `src/Panopticon.Geospatial/Panopticon.Geospatial.csproj` (`Nullable` enabled, no Panopticon project references) and add it with `dotnet sln src/Panopticon.sln add src/Panopticon.Geospatial`.
2. Create `test/Panopticon.Geospatial.Tests` following the Mathematics test project pattern, referencing `Panopticon.Geospatial`, and add it to the solution.
3. Add `<PackageVersion Include="BitMiracle.LibTiff.NET" Version="<latest stable>" />` to `Directory.Packages.props` and a `PackageReference` in the new project.
4. Add `GeoStage` (Manifest, Archive, Parse, Coordinates, Raster, Generate, Write, Validate), `GeoSeverity` (Info, Warning, Error), `GeoDiagnostic` (record: code, stage, severity, message, optional source path, optional `Exception` detail), and `GeoDiagnosticBag` (`Add`, `HasErrors`, ordered `Items`).
5. Add `GeoDiagnosticCodes` with string constants grouped by stage (`GEO-MAN-001` style); later cards append codes here.
6. Add `GeospatialImportLimits` (record with defaults: manifest 1 MiB, KML entry 64 MiB, KMZ 1000 entries / 512 MiB expanded / ratio 100, raster 65536 x 65536 and 4 GiB decoded, 1,000,000 features, 50,000,000 coordinates, 65536 chunks).
7. Add `test/Panopticon.Geospatial.Tests/GeoDiagnosticBagTests.cs` covering ordering and `HasErrors`.

## Files likely to change

- New `src/Panopticon.Geospatial/` and `test/Panopticon.Geospatial.Tests/` projects
- `src/Panopticon.sln`
- `Directory.Packages.props`

## Acceptance criteria

- [ ] `dotnet build src` builds both new projects.
- [ ] `Panopticon.Geospatial` has no `ProjectReference` entries.
- [ ] `GeoDiagnosticBag.HasErrors` is true only when an `Error` diagnostic exists.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Panopticon.Geospatial.Tests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No readers, manifest, or transforms.

## On completion

- Set Status above to `Done` or `Blocked`. Set B4b Status to `In progress` in the track doc.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-02`, `WI-B4b-03`, and `WI-B4b-04`.
