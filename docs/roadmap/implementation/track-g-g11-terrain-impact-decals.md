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

`WI-G11-01`: capped client decal store, original numeric scorch types, floating-point radius,
and FX execution. Original duplicate rejection compares both axes and radius against
`radius / 4`; full stores evict before that check. Store and live FX tests cover the client
random boundary and unload cleanup. Effective parsed nugget counts: Generals 30, Zero Hour 46
(raw archive occurrences include comments or overridden definitions). Focused tests pass with
both installs; group-wide validation remains pending.

`WI-G11-03`: `GameClient.AddScorch` owns random resolution and forwards to terrain. Map
loading consumes objects with an integer `scorchType` property, matching the original
`MO_SCORCH` derivation, and reads the real-valued `objectRadius`. Synthetic ingestion,
duplicate/reload, ordinary-object, and no-terrain client tests pass.

Producer gaps: `NeutronMissileSlowDeathBehaviorModuleData` and
`FirestormDynamicGeometryInfoUpdateModuleData` have no runtime implementation;
`ParticleUplinkCannonUpdate.Update` is a stub. No timing or simulation behaviour was added.

Installed Generals maps carrying scorches: CHI04 (2), CHI05 (53), Dark Mountain (18), GLA01 (2),
GLA02 (1), GLA06 (16), GLA07 (30), Mountain Fox (5), Scorched Earth (51), USA04 (10),
USA07 (48), USA08 (11). The inventory reports existing map parser gaps for Homeland Alliance,
Training01, USA06, and Winter Wolf; those maps were not counted.
