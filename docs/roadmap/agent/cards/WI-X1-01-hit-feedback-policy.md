# Task Card `WI-X1-01`: Hit-feedback policy audit

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X1-01` |
| Roadmap phase | [X1](../../track-x-fixes-tweaks.md): imported-unit hit feedback |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-M2-03` |
| Diagrams | [Architecture](../../deep-dives/x1-imported-unit-hit-feedback.md#architecture-diagrams), reconcile with audit |

## Goal

Specify the smallest explicit presentation policy that binds installed Generals damage effects to the capture's imported EE2 tank.

## Context to read (and only this)

- [Design](../../deep-dives/x1-imported-unit-hit-feedback.md).
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitCombatMaterializer.cs` and `UnitMaterializationContext.cs`.
- `src/Panopticon.Simulation/Logic/Object/ArmorTemplateSet.cs`.
- `src/Panopticon.Simulation/Logic/Object/Body/ActiveBody.cs`: `ValidateArmorAndDamageFX`, `DoDamageFX`.
- `src/Panopticon.Tools.TestHarness/UnitContentImportRunner.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/capture-ee2-reference-roster.json` and its `CaptureContentImport` model.

## Steps

1. Inspect installed `INI.big` entries for `GLARebelMachineGun`, `TankDamageFX`, and referenced FX lists. Record exact names, selected damage type, target placement, and throttle. Do not extract assets into the repository.
2. Trace asset lookup and scoped publication. Specify an opt-in mapping scoped to the selected imported vehicle, where it is recorded, and how missing references produce pre-publication diagnostics. Avoid adding a Generals-native passthrough binding to EE2 source records.
3. Check existing capture health-bar, actor-label, camera, and overlay facilities. Record reuse points and any narrow extension needed.
4. Populate the implementation plan's audit section with exact fields, files, tests, and validation commands. Reconcile diagrams and `WI-X1-02` before setting that card Ready.

## Files likely to change

- `docs/roadmap/implementation/track-x-x1-hit-feedback.md`.
- `docs/roadmap/deep-dives/x1-imported-unit-hit-feedback.md`.
- `docs/roadmap/agent/cards/WI-X1-02-imported-damage-fx-binding.md`.

## Acceptance criteria

- [ ] Installed source assets and runtime binding path are cited; muzzle FX and target FX are distinguished.
- [ ] Policy scope, absence behavior, missing-asset diagnostics, and exact implementation seams are specified.
- [ ] Capture reuse points are recorded; no general EE2 effects importer or projectile rewrite is required.

## Validation commands

Read-only source/archive inspection; validate edited Markdown links and `git diff --check`.

## Out of scope / guardrails

No runtime edits, damage/balance changes, or M2 blocker fixes. Escalate if the mapping cannot stay bounded.

## On completion

Set Done, tick X1-1, set phase In progress, update README progress, and append `.exchange`. Ready the next card only after reconciliation.
