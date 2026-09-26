# Task Card `WI-B4b-17`: Passability and buildability masks

| Field | Value |
|---|---|
| Card ID | `WI-B4b-17` |
| Roadmap phase | [Track B · B4b](../../track-b-scale.md): B4b-6 vector semantics |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-12` and `WI-B4b-16` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Generate 129 x 129 bit-flag mask chunks from slope thresholds and mapped regions.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.1 (masks, exaggeration).

## Steps

1. Add `TerrainMaskFlags` (`Impassable` 1, `Unbuildable` 2) and manifest-driven slope thresholds (default 45 degrees impassable, 15 degrees unbuildable) computed from central differences using the apron.
2. Overlay Water (impassable, unbuildable), Passability, and Buildability regions from `FeatureMapper`, later rules overriding.
3. Record vertical exaggeration and thresholds in the mask report.
4. Add `test/Panopticon.Geospatial.Tests/Generation/TerrainMaskGeneratorTests.cs`.

## Files likely to change

- `src/Panopticon.Geospatial/Generation/TerrainMask*.cs`
- `test/Panopticon.Geospatial.Tests/Generation/`

## Acceptance criteria

- [ ] A plane steeper than 45 degrees is fully impassable; a 10 degree plane has no flags.
- [ ] Changing exaggeration changes the slope-derived mask.
- [ ] Water polygons set both flags.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~TerrainMaskGenerator"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No pathfinding integration.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-18` when their other prerequisites are `Done`.
