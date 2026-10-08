# Task Card `WI-G10-01`: Audit proportions and reference scale

> Audience: agent. Asset-data audit and reproducible measurement contract.

| Field | Value |
|---|---|
| Card ID | `WI-G10-01` |
| Roadmap phase | [Track G · G10](../../track-g-rendering-clarity.md): Realistic proportions |
| Status | `Done` |
| Type | `audit` |
| Effort | `M` |
| Prerequisites | none |
| Diagrams | [G10 architecture](../../deep-dives/g10-realistic-proportions.md#5-architecture-diagrams) |

## Goal

Re-derive the reference world scale and aircraft deviations from installation-resolved INI/W3D
assets. Supply evidence for render-only overrides, not new gameplay dimensions.

## Context to read (and only this)

- [G10 design](../../deep-dives/g10-realistic-proportions.md), including the shared-bone warning.
- [G10 implementation plan](../../implementation/track-g-g10-realistic-proportions.md).
- `src/Panopticon.Simulation/Graphics/ModelInstance.cs` and model mesh bounds consumers.
- `GameInstallation.CreateFileSystem()` and existing W3D/INI loading paths.

## Steps

1. Choose representative infantry, vehicles, buildings and aircraft. Record model/condition
   state, longitudinal axis, mesh exclusions, INI scale and asset hashes.
2. Measure mesh bounds excluding FX. Cross-check INI `Geometry` without treating it as visual size.
3. Record real-world reference lengths and sources, derivation, tolerances and uncertainty.
   Recheck the approximate 5 units/m and 2-2.5x aircraft claims rather than assuming them.
4. Add a reference manifest, deviation table, pure arithmetic tests and an install-required
   data test that fails explicitly when asset hashes or derived measurements change.
5. Trace shared bone/world matrix consumers, weapon launch queries, culling and selection.
   Record a render-only insertion point in the implementation plan.

## Acceptance criteria

- [ ] The table spans all four classes and every measurement is reproducible from the manifest.
- [ ] The data test re-derives measurements using patched/loose-overridden installation assets.
- [ ] Pure arithmetic tests need neither GPU nor install; missing install is explicit in asset tests.
- [ ] The proposed insertion point cannot mutate gameplay bone transforms or query bounds.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Guardrails

No asset redistribution, extraction requirement, gameplay data edit or unreviewed override value.
Changed deterministic artifacts require preservation and user determination.

## On completion

Set this card to `Done`, tick G10-1, append a repo-root `.exchange` result, and unblock
`WI-G10-02` only when its other prerequisites are satisfied. G10 remains unfinished.
