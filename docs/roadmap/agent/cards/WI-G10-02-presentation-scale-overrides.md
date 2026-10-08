# Task Card `WI-G10-02`: Presentation-only scale overrides

> Audience: agent. Render transform and presentation-bounds implementation.

| Field | Value |
|---|---|
| Card ID | `WI-G10-02` |
| Roadmap phase | [Track G · G10](../../track-g-rendering-clarity.md): Realistic proportions |
| Status | `Done` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-G10-01`, `WI-G2-03` |
| Diagrams | [G10 architecture](../../deep-dives/g10-realistic-proportions.md#5-architecture-diagrams) |

## Goal

Apply audited per-definition aircraft scale only when submitting render items. Keep all shared
world/bone transforms, simulation geometry and input-query bounds unchanged.

## Context to read (and only this)

- [G10 design](../../deep-dives/g10-realistic-proportions.md) §3 and diagrams.
- [G10 audit evidence](../../implementation/track-g-g10-realistic-proportions.md).
- `src/Panopticon.Simulation/Graphics/ModelInstance.cs`, render-item constants and culling consumers.
- G2's applied settings and presentation resource lifecycle.

## Steps

1. Add immutable per-definition visual overrides from the reviewed manifest, with identity default.
2. Compose the multiplier into render-item constants at the audited insertion point. Do not
   scale `ModelInstance.SetWorldMatrix` or its `AbsoluteBoneTransforms`.
3. Derive separate presentation culling bounds so enlarged aircraft are not clipped. Keep
   `Scene25D.GetBoundingSphere`, picking, collision, pathing and attack ranges unchanged.
4. Align presentation-only rings/effects where required, without changing their gameplay queries.
5. Test identity, aircraft-only scale, culling and unchanged shared matrices/bone queries.
   Compare existing P0/P9 gameplay assertions and scenario artifacts with the option on/off.

## Acceptance criteria

- [ ] Only audited aircraft visuals change; disabled output follows the identity path.
- [ ] Shared world/bone matrices and launch, selection and collision queries are identical on/off.
- [ ] Enlarged aircraft remain visible at frustum edges with independent presentation bounds.
- [ ] Existing gameplay assertions pass identically; deviations preserve artifacts for user review.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Guardrails

No shared asset mutation, INI `Geometry` or `AttackRange` edit, or global scale multiplier.
Any inability to isolate the visual transform blocks implementation rather than widening scope.

## On completion

Set this card to `Done`, tick G10-2, append a repo-root `.exchange` result, and unblock
`WI-G10-03` when its remaining prerequisite is complete. G10 remains unfinished.
