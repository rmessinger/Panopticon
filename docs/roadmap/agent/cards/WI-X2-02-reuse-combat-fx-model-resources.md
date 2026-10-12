# Task Card `WI-X2-02`: Reduce combat FX/model resource recreation

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X2-02` |
| Roadmap phase | [Track X · X2](../../track-x-fixes-tweaks.md), capacity-probe graphics resource fixes |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S`, one measured recreation owner only |
| Prerequisites | `WI-X2-01` |
| Diagrams | Draft gate: populate class and sequence diagrams for the measured recreation owner before marking Ready |

## Goal

Reduce repeated combat model/FX graphics-resource construction without changing effect appearance or leaking state between instances. Treat this as allocation churn, not an independently proven memory leak.

## Context to read (and only this)

- [Capacity-probe evidence](../../implementation/track-x-x2-capacity-probe-fixes.md#capacity-probe-evidence) and [WI-X2-01 results](WI-X2-01-bound-upload-resource-lifetimes.md#result).
- `src/Panopticon.Simulation/Graphics/ModelInstance.cs`: model-instance construction and ownership.
- `src/Panopticon.Simulation/Graphics/ModelMeshInstance.cs`: per-mesh and per-part constants, callbacks, and resource sets.
- ``src/Panopticon.Simulation/Graphics/ConstantBuffer`1.cs``: allocation and updates.
- `src/Panopticon.Simulation/Logic/Object/Draw/`: narrow to the draw module identified by allocation stacks before marking Ready.
- `src/Panopticon.Simulation/Graphics/ParticleSystems/ParticleSystemManager.cs`: effect creation and disposal.
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`: workload and measurement interval.

## Draft gate

After `WI-X2-01`, capture process-filtered creation counts and stacks for model instances, mesh instances, constant buffers, and resource sets during sustained combat. Identify one owner that recreates equivalent resources repeatedly. Distinguish model-condition changes, muzzle visibility changes, projectile/debris construction, and particle spawning. Do not equate muzzle-named buffers with a per-shot allocation path without evidence.

Replace the provisional draw-module scope with exact files, add grounded class and sequence diagrams, and choose bounded per-owner reuse. If the observed behavior is already bounded or the fix needs general pooling infrastructure, document the result and split or close the card rather than forcing a fix.

## Steps

1. Add a regression for repeated activation/deactivation of the identified effect/model state. Capture construction counts and assert the current recreation behavior.
2. Reuse the compatible model/render resources within that owner. Keep constants and animation state instance-local; retain resources only for a documented owner lifetime with bounded capacity.
3. Define and test reset behavior for animation time, transforms, visibility, tint, opacity, material overrides, and owner/bone attachments as applicable. Dispose retained resources when the owner is destroyed.
4. Repeat the same sustained-combat measurement used by `WI-X2-01`. Report construction counts per interval, managed allocated bytes, native memory trend, and frame-time distribution against the post-`WI-X2-01` baseline.
5. Capture before/after visual evidence for firing and effect completion. Verify no stale muzzle flash, missing effect, or cross-unit state contamination.

## Files likely to change

- The single draw/effect owner named at the Draft gate.
- Focused lifecycle and allocation-count tests under `test/Panopticon.Simulation.Tests/`.
- This card's diagrams and Result section.

## Acceptance criteria

- [ ] Allocation stacks establish the recreation owner and baseline before implementation.
- [ ] Equivalent repeated activations reuse compatible graphics resources instead of allocating them on every transition.
- [ ] Retained resource count is bounded by the documented owner capacity, not elapsed combat time.
- [ ] Reset and owner-destruction tests prevent stale visual state and native-resource leaks.
- [ ] Visual evidence preserves effect timing, attachments, model-condition transitions, and completion.
- [ ] Measured construction counts fall relative to the post-`WI-X2-01` baseline; frame-time and memory results are recorded without invented thresholds.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

Record the exact focused test, Windows D3D11 workload, measurement interval, and screenshot commands in Result. If ReplaySketch is changed, its mandatory screenshot verification includes the map panel.

## Out of scope / guardrails

- No global object pool, broad model cache, suppression of effects, or simulation/weapon timing changes.
- Do not share mutable constant buffers between simultaneously active instances.
- Do not optimize against the pre-`WI-X2-01` leaking baseline.
- Preserve profiler artifacts and follow the repository's determinism-deviation procedure.

## On completion

- Update this card, the [X2 plan](../../implementation/track-x-x2-capacity-probe-fixes.md), checklist, progress table, and phase status if complete.
- Append a result line to repo-root `.exchange`.
- Record any broader pooling proposal as a separate follow-up.

## Result

Pending.
