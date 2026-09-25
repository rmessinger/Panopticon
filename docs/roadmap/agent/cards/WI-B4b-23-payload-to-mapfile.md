# Task Card `WI-B4b-23`: Payload to legacy MapFile preview builder

| Field | Value |
|---|---|
| Card ID | `WI-B4b-23` |
| Roadmap phase | [Track B · B4b](../../track-b-scale.md): B4b-6b dev preview |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-18` and `WI-B4b-22` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Add `Panopticon.Geospatial.Preview` converting a payload or chunk-aligned crop into a resident legacy `MapFile`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §6.1 (dev preview).
- `src/Panopticon.Simulation/Data/Map/MapFile.cs` `CreateFromHeights`.
- `src/Panopticon.Geospatial/Payload/GeneratedMapPayloadReader.cs`.

## Steps

1. Create `src/Panopticon.Geospatial.Preview` referencing `Panopticon.Geospatial` and `Panopticon.Simulation`; add it to the solution and the test project references.
2. Add `GeneratedMapPreviewBuilder.Build(payload, textureMap, crop)`: assemble lattice points for the crop, quantize `round(gameZ / 0.0390625)` minus the crop minimum, clamp to `ushort`, report clamped count and quantization loss.
3. Paint `BlendTileData` textures from `textureMap` (palette name to Generals terrain texture), set passability from masks, add waypoints from mapped Waypoint features.
4. Reject crops that exceed a configurable resident-cell limit with a diagnostic.
5. Add `test/Panopticon.Geospatial.Tests/Preview/GeneratedMapPreviewBuilderTests.cs` using the WI-B4b-18 synthetic payload.

## Files likely to change

- New `src/Panopticon.Geospatial.Preview/`
- `src/Panopticon.sln`
- `test/Panopticon.Geospatial.Tests/`

## Acceptance criteria

- [ ] A synthetic payload builds a `MapFile` whose heights match payload heights within one quantum.
- [ ] Impassable mask cells are impassable in `BlendTileData`.
- [ ] The built map survives `WriteTo` and read-back.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GeneratedMapPreviewBuilder"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No capability gating; dev-only.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-24` when their other prerequisites are `Done`.
