# G12 Implementation: Open Material Packs

> Audience: agent. Card sequence and evidence for [G12](../deep-dives/g12-open-material-packs.md).

## Scope

Operator-built packs of openly licensed materials that replace original terrain textures, add
normal and roughness shading to terrain, and later replace model textures. `Legacy` and the
default material source keep the original look.
Source of truth: [design and diagrams](../deep-dives/g12-open-material-packs.md#5-architecture-diagrams).

## Card sequence

| Card | Sub-phase | Deliverable | Depends on |
|---|---|---|---|
| [WI-G12-01](../agent/cards/WI-G12-01-terrain-texture-inventory-and-map.md) | G12-1 | `TerrainMaterialInventory` runner, `TerrainMaterialMap`, committed mapping table | G7 |
| [WI-G12-02](../agent/cards/WI-G12-02-fetch-open-materials.md) | G12-2 | `fetch_materials.py` with licence records | WI-G12-01 |
| [WI-G12-03](../agent/cards/WI-G12-03-terrain-material-pack-bake.md) | G12-2 | `TerrainMaterialPack`, builder, bake runner, synthetic pack | WI-G12-01, WI-G12-02 |
| [WI-G12-04](../agent/cards/WI-G12-04-terrain-material-runtime-and-goldens.md) | G12-2 | `TerrainMaterialSource` setting, load and fallback, `enhanced-materials` goldens | WI-G12-03 |
| [WI-G12-05](../agent/cards/WI-G12-05-terrain-normal-roughness-shading.md) | G12-3 | Surface map arrays, terrain GGX shading, shared lighting include | WI-G12-04 |
| [WI-G12-06](../agent/cards/WI-G12-06-terrain-pbr-goldens-and-cost.md) | G12-3 | PBR goldens and terrain pass cost | WI-G12-05 |
| [WI-G12-07](../agent/cards/WI-G12-07-model-texture-replacement-investigation.md) | G12-4 | Model texture investigation and G12-4 cards | WI-G12-04 |

`WI-G12-05` and `WI-G12-07` may run in parallel after `WI-G12-04`. Groups on
`staging/track-g-g12`: group 1 `WI-G12-01`..`03`, group 2 `WI-G12-04`, group 3 `WI-G12-05`..`06`,
group 4 `WI-G12-07`.

## Results

Pending.
