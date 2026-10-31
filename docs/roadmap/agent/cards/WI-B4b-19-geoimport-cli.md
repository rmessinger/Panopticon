# Task Card `WI-B4b-19`: GeoImport command-line tool

| Field | Value |
|---|---|
| Card ID | `WI-B4b-19` |
| Roadmap phase | [Track R · R2](../../track-r-real-world-terrain.md): B4b-6a standalone payload |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-18` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Add `Panopticon.Tools.GeoImport` with `import` and `report` verbs over the pipeline.

## Context to read (and only this)

- `src/Panopticon.Tools.Sav2Json/`: small tool project and `CommandLineParser` usage pattern.
- `src/Panopticon.Geospatial/Pipeline/GeospatialImporter.cs`.

## Steps

1. Create `src/Panopticon.Tools.GeoImport` referencing `Panopticon.Geospatial` and `CommandLineParser`; add it to the solution.
2. `import --manifest <path> --out <dir> [--force]`: runs the importer, prints diagnostics one per line (`severity code stage message`).
3. `report --payload <dir>`: prints a summary of `report.json` and chunk counts.
4. Exit codes: 0 success, 1 diagnostics error, 2 usage error; Ctrl+C cancels through the token.
5. Add `test/Panopticon.Geospatial.Tests/Cli/GeoImportCliTests.cs` invoking the entry method in-process.

## Files likely to change

- New `src/Panopticon.Tools.GeoImport/`
- `src/Panopticon.sln`
- `test/Panopticon.Geospatial.Tests/`

## Acceptance criteria

- [ ] `import` on the synthetic manifest exits 0 and creates the payload.
- [ ] A malformed manifest exits 1; a missing argument exits 2.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GeoImportCli"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No preview verb (WI-B4b-24).

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
