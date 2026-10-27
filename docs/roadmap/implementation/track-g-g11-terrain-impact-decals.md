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

`WI-G11-02`: original grid/UV geometry and newest-first alpha order, a priority-5 terrain
overlay, atlas loading, constant Legacy tint, and store/overlapping-height invalidation.
Mesher and live invalidation tests pass. The existing road vertex shader supplies the same
position, normal, cloud UV and view-depth interface, so no duplicate vertex shader was added.
Terrain currently uses the flipped diagonal everywhere; decals follow that topology. The
mesher also tests the original unflipped split, but no separate per-cell flip state is stored
by the current terrain path.

Legacy headless capture inspected: all scorch cells render, conform to the hillside, and stay
below the road pass. Artifact: `~/log/g11-terrain-decals/legacy-v1-5c46e46e957d4b4e9ef90066276827e2/`.
Windowed verification and external sharing remain pending: the shared viewer filesystem is not
mounted on this Mac and no desktop browser is connected. Group-wide golden validation follows.

`WI-G11-04`: Enhanced selects terrain lighting and a 2000-decal cap; Legacy retains its tint
and 500-decal cap. Shared terrain lighting includes shadow visibility; decals also use the
existing cloud term. Settings copies preserve both fields, the controller clamps the limit
to 0..2000, and sampler changes rebuild live decal resource sets.

The `terrain-scorch` scene is registered for both presets. Both local captures render; an
injected zero-decal limit fails `GoldenFrameComparer` at 3.8906% differing pixels against a
0.1% limit. Legacy card-02 versus card-04 captures differ at two pixels by one channel level,
within the canonical tolerance. Artifacts remain under `~/log/g11-terrain-decals/`; canonical
manifest/store entries were not changed because they are pinned to duchessa's AMD/Vulkan
backend. Explicit building-shadow and complete flat/slope/road scene coverage still require
reference-host review before card closeout.

Validation: source solution build passes; 35 focused simulation/settings tests and 20 capture
profile/scene-set tests pass; changed-file `dotnet format`, brace-newline scan, and diff checks
pass. ReplaySketch native screenshot mode also succeeds on macOS; the Map panel remains
functional (`map-panel-v1.png`). No Avalonia UI source was changed.

The broader non-interactive simulation suite finished: 2520 passed, 9 failed, 8 skipped.
Failures: missing installed `.cur`/`.bse` fixtures, WND parsing, SCB/map roundtrips,
parking-place landing, scenario-matrix replay parsing, hijacker ownership, and the A5 benchmark
smoke test. These are outside the changed decal paths; their baseline status has not been
verified. Results: `~/log/g11-terrain-decals/noninteractive-results/g11-noninteractive.trx`.
Broader validation is not clean and canonical goldens remain pending, so G11 is not complete.
