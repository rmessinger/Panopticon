# Task Card `WI-X1-05`: Directional target impacts and Rebel feedback design

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X1-05` |
| Roadmap phase | [X1](../../track-x-fixes-tweaks.md): unblock rendered duel evidence |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-X1-03` |
| Diagrams | [Architecture](../../deep-dives/x1-imported-unit-hit-feedback.md#architecture-diagrams); this read-only card grounds the proposed placement/playback extension before source-edit cards become Ready |

## Goal

Specify a bounded presentation-only fix for attacker-facing tank impacts and visible Rebel hit feedback. Supply the evidence and implementation contracts needed to resume `WI-X1-04`.

## Context to read (and only this)

- [Recorded capture findings](../../implementation/track-x-x1-hit-feedback.md#capture-results-blocked-visible-impact-gate) and [X1 scope](../../deep-dives/x1-imported-unit-hit-feedback.md).
- `src/Panopticon.Simulation/Logic/Object/Damage/DamageFX.cs`: effect selection and victim-origin execution.
- `src/Panopticon.Simulation/Logic/Object/Body/ActiveBody.cs`: `DoDamageFX`, `ValidateArmorAndDamageFX`, damage processing and throttle.
- `src/Panopticon.Simulation/FX/FXListExecutionContext.cs` and `FXNuggets/ParticleSystemFXNugget.cs` under `src/Panopticon.Simulation/FX/`: placement inputs and particle execution.
- `src/Panopticon.Simulation/Logic/Object/IGameObjectClient.cs`: `TryGetModelSpaceBounds`, bone queries.
- `src/Panopticon.Simulation/Client/Drawable.cs`: `TryGetModelSpaceBounds` and draw-module world-transform composition.
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitCombatMaterializer.cs`: typed tank damage and projectile-speed mapping.
- `src/Panopticon.Simulation/Logic/Object/Weapon/WeaponEffects/DamageNugget.cs` and `WeaponTarget.cs` under `src/Panopticon.Simulation/Logic/Object/Weapon/`: real damage dispatch.
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/capture-ee2-reference-roster.json` and `src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs`: reproduction and captured actors.
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitDamageEffectLifecycleTests.cs`: existing graphics-free observation seam.
- Installed `INI.big`: Rebel definition, `InfantryDamageFX`, `TankDamageFX`, and the selected FXLists/particle templates. Inspect archives without committing source assets.

## Steps

1. Trace one hit in each direction. Record the damage type, amount, selected armor set, DamageFX group, FXList, particle template, execution position, and damage frame. Anchor the trace to tank hits at capture frames 201/206 and Rebel hits at 45/152/259. Distinguish source facts, operator observations, and unverified hypotheses.
2. Specify attacker-facing placement from source-to-target direction and transformed rendered model bounds. Reuse an actual contact point if the existing path supplies one. Otherwise label a bounds-derived contact as a presentation approximation, not mesh collision. Define height, rotation, scale, missing source/bounds, degenerate direction, and inside-bounds cases. Do not substitute the undersized gameplay radius or a fixed offset tuned to this capture.
3. Identify the smallest explicit opt-in boundary that preserves unconfigured imported and native Generals placement. Specify where attack direction/contact enters effect execution without changing damage or throttle. Check headless operation without client bounds or graphics resources.
4. Trace Rebel native effect selection and playback independently of shell support. Determine whether the gap is a missing selection/reference, ignored authored placement, particle lifecycle/rendering, or occlusion. Assess the ignored `ParticleSystemFXNugget.Height` field only if evidence connects it to the gap; do not change global behavior merely to make the duel pass.
5. Record the selected design and evidence in the implementation plan. Update grounded class/sequence diagrams for the proposed flow, clearly identifying types that do not yet exist. Create separate small source-edit cards for placement and any demonstrated playback defect, with exact files and tests. Add those prerequisites to X1-4 before readying it.

## Files likely to change

- `docs/roadmap/implementation/track-x-x1-hit-feedback.md`.
- `docs/roadmap/deep-dives/x1-imported-unit-hit-feedback.md`.
- `docs/roadmap/agent/cards/WI-X1-04-readable-duel-capture.md`.
- Follow-up cards under `docs/roadmap/agent/cards/`, only for the evidenced placement/playback fixes.
- Matching checklist, tree, resource links, and progress entries for new cards.

## Acceptance criteria

- [x] Both real-hit paths have source-grounded selection/placement traces; absence of Rebel impact is not attributed to missing shells without evidence.
- [x] Directional placement specifies transformed visual bounds/contact, explicit approximation and fallback behavior, and a scoped opt-in preserving native defaults.
- [x] Rebel playback has an evidenced cause or a concrete unresolved blocker; no unsupported repair is presented as settled design.
- [x] Implementation cards identify exact seams and tests for rotated/scaled targets, missing bounds, target rather than muzzle placement, authored throttle, and unchanged health/damage/death outcomes.
- [x] Diagrams and X1-4 prerequisites match the proposed implementation sequence. Final acceptance still requires a labeled real-rendered duel showing impacts in both directions.

## Validation commands

Read-only code/archive inspection and existing capture inspection. Reproduction command: [capture results](../../implementation/track-x-x1-hit-feedback.md#capture-results-blocked-visible-impact-gate). Validate Markdown links and `git diff --check`; render any edited Mermaid diagrams. No build/test is required for documentation-only changes.

## Out of scope / guardrails

No runtime edits in this card. No synthetic damage events, composited hit flashes, cosmetic shell, EE2 projectile importer, mesh-accurate collision system, general event bus, or balance changes. Projectile materialization is a separate capability and is not a prerequisite for X1-4's existing impact gate. Do not widen scope into M2 aircraft/infantry blockers or silently alter native Generals effects. Preserve determinism/golden deviation artifacts and wait for operator determination.

## On completion

Set Done only after the bounded design and implementation cards are recorded; otherwise set Blocked with the missing evidence. Tick X1-5, update progress and `.exchange`, and keep X1-4 Blocked until its implementation prerequisites and rendered acceptance criteria pass.
