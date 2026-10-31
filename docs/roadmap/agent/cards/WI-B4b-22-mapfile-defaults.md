# Task Card `WI-B4b-22`: Legacy MapFile default factories

| Field | Value |
|---|---|
| Card ID | `WI-B4b-22` |
| Roadmap phase | [Track R · R2](../../track-r-real-world-terrain.md): B4b-6b dev preview |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-21` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Add engine factories so code can build a complete, writable legacy `MapFile` from a height grid.

## Context to read (and only this)

- `docs/roadmap/implementation/track-b-b4b-geospatial.md` `Agent context: minimum playable map`.
- Files named in that section.

## Steps

1. Add `HeightMapData.Create16Bit(border, ushort[,] elevations)` setting the version that selects 16-bit elevations.
2. Add `BlendTileData.CreateUniform(width, height, textureName)` with one texture and default passability.
3. Add minimal `WorldInfo` and `SidesList` defaults per the audit (neutral side plus the skirmish players the audit names).
4. Add `MapFile.CreateFromHeights(heights, border, textureName)` composing the above.
5. Add `test/Panopticon.Simulation.Tests/Data/Map/MapFileCreateFromHeightsTests.cs`: create, `WriteTo` a memory stream, read back, compare heights and sections.

## Files likely to change

- `src/Panopticon.Simulation/Data/Map/`
- `test/Panopticon.Simulation.Tests/Data/Map/`

## Acceptance criteria

- [ ] A 257 x 257 generated map writes and reads back with identical heights.
- [ ] `CreateEmpty` behaviour is unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~MapFileCreateFromHeights"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No geospatial references in `Panopticon.Simulation`.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-23` when their other prerequisites are `Done`.
