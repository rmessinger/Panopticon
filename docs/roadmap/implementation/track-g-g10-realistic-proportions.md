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

## Implementation (WI-G10-02, complete)

Overrides live in `VisualScaleOverrides` (`src/Panopticon.Simulation/Graphics/VisualScaleOverrides.cs`),
keyed by object-definition name (ordinal-ignore-case, identity for unlisted names). Aircraft only:

| Object | Multiplier | Derivation |
|---|---|---|
| `ChinaJetMiG` | 1.9149 | 17.32 m / (33.0134 u / 3.6501 u/m) |
| `AmericaVehicleComanche` | 1.1828 | 14.28 m / (44.0651 u / 3.6501 u/m) |

Scale threads through the audited render-only path: `W3dModelDraw.BuildRenderList` computes
`GetPresentationScale()` (identity unless `GraphicsSettings.RealisticProportions` and the definition is
listed) and passes it through `W3dModelDrawConditionState.BuildRenderList` and
`ModelInstance.BuildRenderList` into `ModelMesh.BuildRenderList`, which composes
`Matrix4x4.CreateScale(scale) * meshWorldMatrix` (row-vector, scale before world, about the model
origin at the audited ground plane). Shared matrices are untouched: `SetWorldMatrix`,
`AbsoluteBoneTransforms`, and `_worldMatrix` keep their gameplay values, so weapon-launch bones,
selection, pathing, and collision queries are identical with the option on or off. Presentation culling
follows automatically because `BuildRenderListWithWorldMatrix` derives the render-item bounds from
the scaled world matrix. Particle FX align by value: `ParticleSystemManager.Create` accepts an
optional scale provider and `ParticleSystem.Update` composes the scale into its own world transform
copy when it is not 1, leaving the shared bone matrix read-only. Both presets default the flag off, so
every default session and golden runs the identity path.

## Evidence (WI-G10-02)

- `PresentationScaleTests` (5 tests): manifest cross-derivation for both multipliers, identity for
  ground/null/empty names, case-insensitive keys, bit-equal identity build versus
  `AbsoluteBoneTransforms`, and a scaled build proving `RenderItem.World == CreateScale(2) * bone`
  with doubled bounds while shared matrices and a subsequent identity build stay unchanged. The
  cross-derivation test caught an inverted override formula before commit.
- Suite: 53 + 355 + 2131 passed, 7 skipped (`Category!=Interactive`); format, brace, and
  whitespace checks clean.
- Visual regression gate: 16/16 goldens passed with no regeneration (option off in both presets;
  the gate doubles as the off-path proof).

## Validation and risks

Use installation-resolved assets with patch/loose overrides. Pure arithmetic tests need no install;
asset-data tests require it and fail explicitly on changed assets. Never modify shared model/world
or absolute-bone matrices. Compare gameplay queries and scenario artifacts on/off; suspected
determinism deviations require preservation and user determination. Both presets default the flag off.
