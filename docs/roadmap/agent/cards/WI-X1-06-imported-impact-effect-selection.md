# Task Card `WI-X1-06`: Imported impact-effect selection

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X1-06` |
| Roadmap phase | [X1](../../track-x-fixes-tweaks.md) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-X1-05` |
| Diagrams | [Architecture](../../deep-dives/x1-imported-unit-hit-feedback.md#architecture-diagrams) |

## Goal

Imported weapons opted in by the capture select native target effects through `DamageInfoInput.DamageFXOverride`, so the tank's hits play Rebel impacts without changing damage.

## Context to read (and only this)

- [Rebel feedback decision](../../implementation/track-x-x1-hit-feedback.md#decision-rebel-feedback).
- `src/Panopticon.Tools.ReplaySketch.Core/Model/Capture/CaptureContentImport.cs`: `DamageEffects` pattern.
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitMaterializationContext.cs`: `ValidateDamageEffects`.
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitCombatMaterializer.cs` and `UnitRuntimeMaterializer.cs`.
- `src/Panopticon.Simulation/Logic/Object/Weapon/WeaponEffects/DamageNugget.cs` and `src/Panopticon.Simulation/Logic/Object/Weapon/WeaponTarget.cs`.
- `src/Panopticon.Simulation/Logic/Object/Body/ActiveBody.cs`: `DoDamageFX`.
- `src/Panopticon.Tools.TestHarness/UnitContentImportRunner.cs`.
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitDamageEffectPolicyTests.cs` and `UnitDamageEffectLifecycleTests.cs`.

## Steps

1. Add `CaptureContentImport.ImpactEffectDamageTypes` (unit name to Generals damage type name, default empty). Pass it through `UnitContentImportRunner` as source-qualified unit ids, mirroring `DamageEffects`.
2. Add an optional context mapping. Validate before staging, beside `ValidateDamageEffects`: typed unit in the closure with at least one resolved weapon; name parses through `EngineEnumNames`; `UNRESISTABLE` is rejected as the no-override sentinel. Diagnostics name the subject and value.
3. Add `DamageNugget.DamageFXOverride` (internal set, default `DamageType.Unresistable`). Pass it to `WeaponTarget.DoDamage` and into `DamageInfoInput.DamageFXOverride`. Native parsed nuggets keep the default.
4. In `UnitRuntimeMaterializer`, after weapons are staged, set the override on the nuggets of the mapped unit's own weapons. A weapon shared with an unmapped unit is a validation error rather than silently spreading the mapping.
5. Opt the capture profile in: `"ImpactEffectDamageTypes": { "LightMounted1UpgradeEpoch15": "ARMOR_PIERCING" }`, with a comment marking it as a Generals presentation compatibility choice.

## Files likely to change

- The context, combat/runtime materializers, `DamageNugget.cs`, `WeaponTarget.cs`, `CaptureContentImport.cs`, `UnitContentImportRunner.cs`, `capture-ee2-reference-roster.json`.
- Focused tests under `test/Panopticon.Simulation.Tests/Content/Importing/Units/`.

## Acceptance criteria

- [ ] Mapped weapon executes the infantry effect selected by the override; the same weapon unmapped selects `None` for `InfantryDamageFX`-style groups.
- [ ] Health traces, damage count, and death are identical with and without the override.
- [ ] Unknown names, `UNRESISTABLE`, non-weapon subjects, and shared weapons fail before staging with subject and value named.
- [ ] Native weapons and unmapped imports keep `DamageFXOverride == Unresistable`.
- [ ] Standard validation passes or pre-existing failures are documented against the base.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not change `Ee2ImportPolicy.DamageType`, armor, or damage amounts. No projectile, cosmetic shell, or global effect default. Preserve determinism/golden deviation artifacts for the operator.

## On completion

Set Done, tick X1-6, update progress and `.exchange`.
