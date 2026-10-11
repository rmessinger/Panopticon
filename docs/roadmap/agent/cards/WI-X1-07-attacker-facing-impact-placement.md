# Task Card `WI-X1-07`: Attacker-facing impact placement

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X1-07` |
| Roadmap phase | [X1](../../track-x-fixes-tweaks.md) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-X1-05` |
| Diagrams | [Architecture](../../deep-dives/x1-imported-unit-hit-feedback.md#architecture-diagrams) |

## Goal

Damage effects on X1-mapped imported targets play on the attacker-facing side of the transformed rendered bounds; every other target keeps origin placement.

## Context to read (and only this)

- [Placement decision](../../implementation/track-x-x1-hit-feedback.md#decision-attacker-facing-placement).
- `src/Panopticon.Simulation/Logic/Object/ArmorTemplateSet.cs`: `CreateImported`.
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitCombatMaterializer.cs`: `AddSets`.
- `src/Panopticon.Simulation/Logic/Object/Body/ActiveBody.cs`: `DoDamageFX`, `ValidateArmorAndDamageFX`.
- `src/Panopticon.Simulation/Logic/Object/Damage/DamageFX.cs`: `DoDamageFX`.
- `src/Panopticon.Simulation/Logic/Object/IGameObjectClient.cs`: `TryGetModelSpaceBounds`.
- `src/Panopticon.Simulation/Logic/Object/Weapon/WeaponEffects/ProjectileNugget.cs`: existing logic-side bounds use.
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitDamageEffectLifecycleTests.cs`.

## Steps

1. Add internal enum `DamageFXPlacement { VictimOrigin, AttackerFacingBounds }` and an internal `ArmorTemplateSet.DamageFXPlacement` property (default `VictimOrigin`). `CreateImported` sets `AttackerFacingBounds` only when a DamageFX reference is supplied.
2. Add a pure static helper (for example `DamageFXContact.TryGetAttackerFacingPoint(AxisAlignedBoundingBox localBounds, Matrix4x4 localToWorld, Vector3 sourceWorld, out Vector3 contact)`) implementing the decision: invert, cast from bounds center to the local source, slab-exit point, transform back; source inside or degenerate direction returns the world bounds center; non-invertible or non-finite input returns false.
3. Track the current armor set's placement in `ActiveBody.ValidateArmorAndDamageFX`. In `DoDamageFX`, after the throttle check, compute the position only for `AttackerFacingBounds` with a live source and client bounds, using `ModelTransform.Matrix * Transform.Matrix`. Otherwise use `victim.Translation`.
4. Add a `DamageFX.DoDamageFX` overload taking the position; the existing signature delegates with `victim.Translation`.

## Files likely to change

- `ArmorTemplateSet.cs`, `ActiveBody.cs`, `DamageFX.cs`, one new helper file under `src/Panopticon.Simulation/Logic/Object/Damage/`.
- Focused tests under `test/Panopticon.Simulation.Tests/` (helper geometry plus lifecycle).

## Acceptance criteria

- [x] Helper tests: axis-aligned, yawed, and non-uniformly scaled boxes return the facing-face point; source inside bounds returns the center; singular matrix returns false.
- [x] Lifecycle test with a stub client: mapped target's effect position equals the helper result on the attacker side, not the muzzle or origin; no client, or a destroyed source, falls back to origin.
- [x] Native and unmapped armor sets keep `VictimOrigin`; existing placement and throttle assertions still pass.
- [x] Health/damage/death traces are unchanged with and without placement.
- [x] Standard validation passes or pre-existing failures are documented against the base.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

No mesh collision, physics change, fixed capture offsets, or gameplay-radius substitution. Do not implement `ParticleSystemFXNugget.Height` (global native change, recorded separately). Logic state must not depend on client bounds beyond the effect position.

## On completion

Set Done, tick X1-7, update progress and `.exchange`. X1-4 becomes Ready once X1-6 and X1-7 are Done.

## Result

Landed, then refined after operator review of the first capture: the merged-bounds face sat about a meter ahead of the tank (the gun barrel extends the box), so placement now pins to the entry face of the largest crossed sub-object box via the part-bounds path. Three contact-geometry tests cover the hull-over-barrel, yawed/scaled, and inside-part cases; the lifecycle test drives a real nugget with hull plus barrel parts and asserts the contact stays inside the hull extent away from the origin. See the placement refinement section of the [X1 plan](../../implementation/track-x-x1-hit-feedback.md#placement-refinement-after-operator-review).
