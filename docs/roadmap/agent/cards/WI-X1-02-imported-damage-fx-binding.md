# Task Card `WI-X1-02`: Imported-vehicle damage effects

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X1-02` |
| Roadmap phase | [X1](../../track-x-fixes-tweaks.md) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-X1-01` |
| Diagrams | [Architecture](../../deep-dives/x1-imported-unit-hit-feedback.md#architecture-diagrams) |

## Goal

Apply the audited compatibility policy through shared materialization so the imported tank uses existing target-side damage effects.

## Context to read (and only this)

- [Audit and design](../../implementation/track-x-x1-hit-feedback.md#audit-and-results).
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitCombatMaterializer.cs` and `UnitMaterializationContext.cs`.
- `src/Panopticon.Simulation/Logic/Object/ArmorTemplateSet.cs`.
- `src/Panopticon.Tools.TestHarness/UnitContentImportRunner.cs` and the audited `CaptureContentImport` definition.

## Steps

1. Add `CaptureContentImport.DamageEffects` and context source-qualified mappings plus an installed DamageFX lookup, as specified in the audit. Imports without a mapping remain unchanged.
2. Bind validated effects on the imported vehicle's armor set. Use a narrow construction seam if needed; do not weaken unrelated native setters or overwrite Generals assets.
3. Validate requested references before publication. A missing requested effect must not publish partial content.
4. Add a focused success/absence/missing-reference test. Record exact landed names and reconcile `WI-X1-03`.

## Files likely to change

- Audit-approved capture-import model, materialization policy/context, `UnitCombatMaterializer.cs`, and `UnitContentImportRunner.cs`.
- `src/Panopticon.Simulation/Logic/Object/ArmorTemplateSet.cs`, only if the audit requires a construction seam.
- Focused tests under `test/Panopticon.Simulation.Tests/Content/Importing/Units/`.

## Acceptance criteria

- [ ] Explicit tank mapping binds the requested effect; unconfigured imports do not gain effects.
- [ ] Missing requested assets fail before publication with the subject and asset name.
- [ ] Generals bindings and damage/armor/weapon values remain unchanged.
- [ ] Standard validation passes or pre-existing failures are documented against the base.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

No fake projectile, global default effects, direct synthetic damage events, or source-authored EE2 effect claims. Preserve headless execution. Suspected determinism deviations require artifact preservation and operator review.

## On completion

Set Done, tick X1-2, update progress and `.exchange`, and reconcile the next card.
