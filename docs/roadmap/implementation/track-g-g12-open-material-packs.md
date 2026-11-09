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
| [WI-G12-01](https://projects.reid-corp.com/work_packages/584) | G12-1 | `TerrainMaterialInventory` runner, `TerrainMaterialMap`, committed mapping table | G7 |
| [WI-G12-02](https://projects.reid-corp.com/work_packages/585) | G12-2 | `fetch_materials.py` with licence records | WI-G12-01 |
| [WI-G12-03](https://projects.reid-corp.com/work_packages/586) | G12-2 | `TerrainMaterialPack`, builder, bake runner, synthetic pack | WI-G12-01, WI-G12-02 |
| [WI-G12-04](https://projects.reid-corp.com/work_packages/587) | G12-2 | `TerrainMaterialSource` setting, load and fallback, `enhanced-materials` goldens | WI-G12-03 |
| [WI-G12-05](https://projects.reid-corp.com/work_packages/588) | G12-3 | Surface map arrays, terrain GGX shading, shared lighting include | WI-G12-04 |
| [WI-G12-06](https://projects.reid-corp.com/work_packages/589) | G12-3 | PBR goldens and terrain pass cost | WI-G12-05 |
| [WI-G12-07](https://projects.reid-corp.com/work_packages/590) | G12-4 | Model texture investigation and G12-4 cards | WI-G12-04 |
| [WI-G12-08](https://projects.reid-corp.com/work_packages/1004) | G12-5 | Sky HDRI fetch, `SkyPackBuilder`, `SkyPack`, synthetic sky pack | WI-G12-03 |
| [WI-G12-09](https://projects.reid-corp.com/work_packages/1005) | G12-5 | `SkySource` setting, `SkyMap`, sky-pack resolver | WI-G12-08, WI-G13-05 |

`WI-G12-05` and `WI-G12-07` may run in parallel after `WI-G12-04`. Stack groups: group 1
`WI-G12-01`..`03`, group 2 `WI-G12-04`, group 3 `WI-G12-05`..`06`, group 4 `WI-G12-07`, group 5
`WI-G12-08`..`09` (after the G13 group holding `WI-G13-05` merges).

## Results

Pending.
