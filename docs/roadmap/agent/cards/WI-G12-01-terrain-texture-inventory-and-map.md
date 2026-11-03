# Task Card `WI-G12-01`: Terrain texture inventory and material map

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G12-01` |
| Roadmap phase | [Track G · G12](../../track-g-rendering-clarity.md): Open material packs |
| Status | `Ready` |
| Type | `implementation` |
| Effort | `S` |
| Prerequisites | G7 offline terrain texture packs (Done) |
| Diagrams | [G12 architecture](../../deep-dives/g12-open-material-packs.md#5-architecture-diagrams) |

## Goal

Report every original `Terrain` entry and commit the mapping table that assigns open material sources by class, so later cards bake replacements from data instead of hand lists.

## Context to read (and only this)

- [G12 design](../../deep-dives/g12-open-material-packs.md) sections 1, 2, and 3.1.
- `src/Panopticon.Simulation/Terrain/TerrainTexture.cs`, `Terrain.cs:513-670`.
- `src/Panopticon.Tools.TestHarness/TerrainTextureBakeRunner.cs` (INI parse and image resolution to copy).

## Steps

1. Add `TerrainMaterialInventoryRunner` and `BuiltInScenario.TerrainMaterialInventory`: parse `Data/INI/Terrain.ini`, resolve each image through the installation filesystem, and write JSON plus a text table: name, class, texture, resolved path or `missing`, size, bit depth, linear mean RGB, linear standard deviation, and the distinct `BlendTileTexture.CellSize` values used for that entry across shipped maps.
2. Add `TerrainMaterialMap` (record types plus `Load`, `Validate`, `Resolve(name, class)`, and a content `Hash`) for the table in design 3.1, and commit `src/Panopticon.Tools.TestHarness/terrain-material-map.json` with `classDefaults` for all 15 classes, `overrides` where the inventory shows an entry's mean colour far from its class, and `keepOriginal` for markings and `TRANSITION` entries. Source ids name ambientCG or Poly Haven materials; record the licence per id.
3. Validation rejects unknown terrain names, unknown classes, duplicate overrides, and source ids without a licence.
4. Run against Generals and Zero Hour; record entry counts, missing images, and class coverage in the G12 plan Results.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/TerrainMaterialInventoryRunner.cs`, `BuiltInScenario.cs`, `HarnessScenarioCatalog.cs`, `Program.cs`
- `src/Panopticon.Simulation/Content/TerrainMaterialMap.cs` (new)
- `src/Panopticon.Tools.TestHarness/terrain-material-map.json` (new)
- `test/Panopticon.Simulation.Tests/Content/TerrainMaterialMapTests.cs` (new)

## Acceptance criteria

- [ ] The inventory lists 236 Generals entries with sizes and colour statistics, and names every unresolved image.
- [ ] Every class resolves to a source or `keepOriginal`; validation tests cover each rejection.
- [ ] No texture or derived image is committed.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario TerrainMaterialInventory --headless
```

## Out of scope / guardrails

- Downloading materials (`WI-G12-02`) and baking (`WI-G12-03`).
- Model textures (`WI-G12-07`).

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-G12-02` to `Ready`.
- Tick G12-1 in the checklist and update the README progress row.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
