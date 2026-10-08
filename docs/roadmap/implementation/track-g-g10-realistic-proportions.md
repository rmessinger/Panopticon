# G10 Implementation: Realistic Proportions

> Audience: agent. Card sequence and evidence for [G10](../deep-dives/g10-realistic-proportions.md).

## Scope

Reproducible asset reference data and explicit opt-in render-only aircraft scaling.
Source of truth: [design and diagrams](../deep-dives/g10-realistic-proportions.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| [WI-G10-01](../agent/cards/WI-G10-01-audit-proportions-and-reference-scale.md) | Reference manifest, deviation table and measurement tests | none |
| [WI-G10-02](../agent/cards/WI-G10-02-presentation-scale-overrides.md) | Per-definition visual transform and separate culling bounds | WI-G10-01, WI-G2-03 |
| [WI-G10-03](../agent/cards/WI-G10-03-preset-wiring-and-golden-frames.md) | Explicit opt-in and proportion golden captures | WI-G10-02, WI-G3-04 |

## Audit (WI-G10-01, complete)

Produced by WI-G10-01 against track-g9-3-chrome (a49a770ed). Reference data lives in
`test/Panopticon.Simulation.Tests/Data/Graphics/Proportions/reference-scale-manifest.json`
(copied to the test output; never written back). Tests: `ReferenceScaleArithmeticTests`
(pure, no install) and `ReferenceScaleManifestTests` (installation re-derivation, explicit
failure on any drift).

### Reference manifest

| Object | Class | Model (default state) | Exclusions | X | Y | Z | Real reference |
|---|---|---|---|---|---|---|---|
| AmericaInfantryRanger | Infantry | AIRngr_SKN | FX | 3.6968 | 19.5141 | 14.6085 | 1.75 m standing height (NATO median) |
| AmericaTankCrusader | Vehicle | AVLeopard | FX | 44.3610 | 17.2658 | 10.8544 | 7.93 m M1 Abrams hull (anchor) |
| AmericaVehicleComanche | Aircraft | AVComanche | FX, AVCOMANCHE_PROP, NVBUGGY_M | 44.0651 | 15.7483 | 9.8569 | 14.28 m RAH-66 Comanche |
| ChinaJetMiG | Aircraft | NVMIG | FX | 33.0134 | 23.2193 | 6.5846 | 17.32 m MiG-29 |
| AmericaPowerPlant | Building | ABPWRPLANT | FX | 45.7040 | 61.9476 | 41.8327 | none (consistency datapoint) |

All INI `Scale` values are 1. Axes: X longitudinal (forward), Y lateral (wingspan, arm span),
Z up (all models sit near the Z=0 ground plane). Crusader hull anchor excludes BARREL01 and
HOUSECOLOR05 (muzzle trim reaches the same X maximum as the gun): hull span 28.9443 units.
Comanche fuselage excludes the rotor-blur disk AVCOMANCHE_PROP (52-unit diameter) and
NVBUGGY_M; a generic FX rule is insufficient. Manifest carries the SHA-256 of each
`art/w3d/<model>.w3d` archive entry.

### Deviation table

Baseline: Crusader hull 28.9443 units / 7.93 m = 3.6501 units/m.

| Object | Measured | Rendered at 3.6501 u/m | Reference | Deviation |
|---|---|---|---|---|
| Ranger height | 14.6085 | 4.00 m | 1.75 m | 2.29x oversized |
| Crusader hull | 28.9443 | 7.93 m | 7.93 m | anchor by construction |
| MiG length | 33.0134 | 9.04 m | 17.32 m | needs 1.92x upscale |
| Comanche fuselage | 44.0651 | 12.07 m | 14.28 m | needs 1.18x upscale |
| Power plant footprint | 45.704 | 12.52 m | none | plausible plant footprint |

Claim re-derivation: the "~5 units/m" community baseline is not supported by the hull anchor
(3.6501 derived; 5 over-scales by 37%). The "2-2.5x aircraft" claim holds at both baselines
for jets: MiG needs 1.92x at 3.6501 u/m and 2.62x at 5 u/m; the helicopter needs less
(1.18x-1.62x). Infantry and jets do not share any single units-per-metre constant
(infantry 8.35 u/m by height vs jet 1.91 u/m by length, ratio 4.4x), which is the premise
of this track phase: per-class presentation scale, not a global constant.

### Insertion point for WI-G10-02 (render-only)

`ModelInstance.BuildRenderList` passes the gameplay `_worldMatrix` per subobject;
`ModelMesh.BuildRenderList` picks `Skinned ? modelTransform : AbsoluteBoneTransforms[parent]`
and builds render items with visual culling bounds via
`AxisAlignedBoundingBox.Transform(BoundingBox, world)`.

Constraints verified:

- `ModelInstance.SetWorldMatrix` writes `_worldMatrix` AND recomputes `AbsoluteBoneTransforms`
  consumed by weapon-launch bones (`W3dModelDraw.cs:357` particle launch, `GetWeaponLaunchBone`)
  and `W3dModelDraw.SetWorldMatrix` (`:506`) applies `GameObject.VerticalOffset`. None of these
  may observe a presentation scale.
- Scale composes only at the render-item world constant (row-vector order: scale BEFORE world),
  with a presentation-side bone-transform variant for non-skinned meshes; skinned meshes take
  the scale on the instance world matrix alone.
- Culling bounds derive separately (transform the visual box by the scaled matrix) so frustum
  and shadow culling stay correct without touching gameplay queries.

## Validation and risks

Use installation-resolved assets with patch/loose overrides. Pure arithmetic tests need no install;
asset-data tests require it and fail explicitly on changed assets. Never modify shared model/world
or absolute-bone matrices. Compare gameplay queries and scenario artifacts on/off; suspected
determinism deviations require preservation and user determination. Both presets default the flag off.
