# G11 Implementation: Terrain Impact Decals

> Audience: agent. Card sequence and evidence for [G11](../deep-dives/g11-terrain-impact-decals.md).

## Scope

Persistent scorch marks from FX, logic modules, and map objects, drawn as a terrain overlay with
the original look under `Legacy` and terrain-lit under `Enhanced`.
Source of truth: [design and diagrams](../deep-dives/g11-terrain-impact-decals.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| [WI-G11-01](../agent/cards/WI-G11-01-terrain-decal-store-and-scorch-fx.md) | Scorch enum and parse fixes, `TerrainDecalStore`, nugget `Execute`, INI data test | WI-G2-03 |
| [WI-G11-02](../agent/cards/WI-G11-02-terrain-decal-mesh-and-pass.md) | Original-geometry overlay mesh, `TerrainDecals` bucket and shaders, `Legacy` tint | WI-G11-01, WI-G3-04 |
| [WI-G11-03](../agent/cards/WI-G11-03-logic-and-map-scorch-producers.md) | Client `AddScorch`, neutron/uplink/firestorm producers, map-placed scorches | WI-G11-01 |
| [WI-G11-04](../agent/cards/WI-G11-04-enhanced-decals-and-golden-frames.md) | `Enhanced` terrain lighting and limit, `vr-terrain-scorch` goldens | WI-G11-02, WI-G11-03 |

`WI-G11-02` and `WI-G11-03` may run in parallel after `WI-G11-01`.

## Results

Pending `WI-G11-01`.
