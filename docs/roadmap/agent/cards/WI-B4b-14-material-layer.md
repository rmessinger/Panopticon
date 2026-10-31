# Task Card `WI-B4b-14`: Material layer generator

| Field | Value |
|---|---|
| Card ID | `WI-B4b-14` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-5 terrain and imagery generation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-12` and `WI-B4b-15` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Generate 129 x 129 material-index chunks from classification rasters and polygon rules.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.2 (material rules).

## Steps

1. Add `MaterialPalette` (ordered distinct material names from the manifest, default first; index is `byte`, max 255 entries).
2. Add `MaterialLayerGenerator.Generate(grid, classification, clippedPolygons, rules, cx, cy)`: nearest-kernel classification lookup, then polygon rules (point-in-polygon at lattice points), later rules overriding earlier; unmatched cells use the default.
3. Add `test/Panopticon.Geospatial.Tests/Examples/worldcover-materials.json`, an example manifest fragment mapping ESA WorldCover classes 10-100 to palette names, and a test that loads it through `ManifestLoader`; it is an example, not a built-in.
4. Add `test/Panopticon.Geospatial.Tests/Generation/MaterialLayerGeneratorTests.cs`.

## Files likely to change

- `src/Panopticon.Geospatial/Generation/Material*.cs`
- `test/Panopticon.Geospatial.Tests/Examples/`
- `test/Panopticon.Geospatial.Tests/Generation/`

## Acceptance criteria

- [ ] Polygon rules override classification rules at covered lattice points.
- [ ] Unknown palette names referenced by rules fail with a `Generate` error.
- [ ] Cells matching no rule use the default material index 0.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~MaterialLayerGenerator"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No blend-tile or legacy texture mapping.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-18` when their other prerequisites are `Done`.
