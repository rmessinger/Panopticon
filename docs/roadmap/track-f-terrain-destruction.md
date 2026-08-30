# Track F — Deformable Terrain & Destruction

> Part of the [Large-Scale RTS Roadmap](README.md). Runtime terrain deformation (craters/scorch
> from explosions) and physics-based structural destruction (rigid-body debris/collapse) that
> replaces the existing model-swap destruction pipeline. This is new fidelity beyond original SAGE
> (no terrain deformation; buildings destroyed by swapping to pre-authored rubble/damage-state
> models), so it is not gated by Track P playable-baseline fidelity. Split as its own track because
> it cuts across terrain storage, rendering, a new physics substrate, combat, and save/replay (see
> the [scope Decision](open-questions.md)).
>
> Audience: human; strategic track overview.

## Phases

> The Milestone column states the single demonstrable outcome that marks each phase done (its
> definition-of-done); the granular steps to get there live in
> [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| F1. Terrain deformation data model | Define an engine-internal representation for terrain deltas (crater/scorch profiles layered on the heightmap grid) that composes with [B4a's chunked heightmap storage](deep-dives/b4a-larger-maps.md) without requiring the whole grid resident. | A deformation delta can be applied to and queried from a chunked heightmap region in isolation from rendering. | Not started | Track P (P7), B4a-5 |
| F2. Runtime mesh & rendering update | Regenerate affected `TerrainPatch` mesh/normals/blend data for deformed regions at runtime, LOD-aware, without rebuilding the whole terrain. | A deformation applied at runtime is visible in the rendered terrain within one frame budget, with only affected patches rebuilt. | Not started | F1, B5 |
| F3. Weapon/combat-driven deformation | Wire explosive weapon hits through the existing `DamageModule`/weapon pipeline to apply a deformation (crater/scorch) sized by weapon/damage parameters. | An explosive weapon impact produces a crater of the expected radius/depth via the existing combat pipeline, with no change to non-explosive weapons. | Not started | F1, P6-2 |
| F4. Physics engine integration | Introduce a pluggable rigid-body physics seam (BepuPhysics 2.4, per the [seam plan](plans/physics-seam.md)) behind an engine-internal interface, runnable both windowed and headless, with its own benchmark harness. No gameplay uses it yet; it proves the seam. | A rigid-body simulation (e.g. a falling/stacking debris test scene) runs through the seam in both windowed and headless modes and is measured by the benchmark harness. | In progress: seam + Bepu backend + `PhysicsStack` debris scene landed and validated headlessly (settle bounds, stability probe, ms/Timestep at 100/1k/10k bodies); windowed run and B1-harness benchmarks pending | Track P (P7), A1, B1 |
| F5. Physics-based structural destruction | Replace the canned `RubbleRiseUpdate`/`ToppleUpdate` model-swap on building death with rigid-body debris/collapse simulated through the F4 physics seam (procedural or pre-authored fracture pieces), sized/shaped by the destroyed object's geometry and the destroying weapon. | A destroyed building collapses into simulated rigid-body debris (not a single pre-baked rubble model) that comes to rest via physics, replacing the existing damage-state model swap for at least one building type. | Not started | F4, P6-3 |
| F6. Destruction-terrain interaction | Tie physics-based destruction (F5) and terrain deformation (F1) together so settled debris/craters affect pathing (blocked/impassable cells) and object placement. | A destroyed building's settled debris and any crater block pathing and prevent new construction on the same footprint until cleared. | Not started | F1, F3, F5, P6-3 |
| F7. Persistence & replay | Persist terrain deltas and physics debris resting state through `StatePersister` (save/load), and ensure ReplaySketch/the P0 harness capture deformation and destruction events for inspection and validation against authored bounds. | A save/load round-trip preserves terrain deformation and debris resting state; a ReplaySketch-authored scenario with an explosive weapon and a destroyed building asserts the resulting crater and debris state headlessly within loose bounds. | Not started | F1, F3, F5 |
| F8. Authoring & validation tooling | Extend ReplaySketch (no dedicated viewer, per the [C5 tooling Decision](open-questions.md) no-fragmentation convention) to preview and headlessly assert terrain deformation and physics-based destruction state (crater presence/radius/depth, debris settled bounds) via screenshot mode and the P0 assertion layer. | ReplaySketch screenshot mode shows deformed terrain and settled debris, and a headless assertion validates crater radius/depth and debris bounds against the triggering weapon/building. | Not started | F2, F6, F7 |

## Phase resources

A navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams are required before any phase's first task card is marked `Ready`; all F-phase deep
dives are yet to be written.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| F1 | — | — | — |
| F2 | — | — | — |
| F3 | — | — | — |
| F4 | — | — | — |
| F5 | — | — | — |
| F6 | — | — | — |
| F7 | — | — | — |
| F8 | — | — | — |

## Cross-track dependencies

Hard dependencies are in the phase table above; these are the coordination notes:

- F2 patch-mesh regeneration reuses [B5](track-b-scale.md) streaming/LOD rather than a parallel
  pipeline.
- F5/F6 coordinate with [B7 variable fidelity](track-b-scale.md): full rigid-body simulation is
  expensive at scale, so distant/off-screen destruction needs a cheaper fallback (near today's
  canned model swap) selected by the same fidelity-policy seam, not a second LOD mechanism.
- F7 asserts physics-debris outcomes by loose authored bounds (e.g. debris resting-position
  radius); cross-run divergence of physics outcomes is acceptable, so no
  [A2](track-a-foundations.md) tolerance machinery is required for Track F.
- F8 extends the [P0 ReplaySketch harness](deep-dives/p0-playable-baseline-harness.md); no new
  authoring tool (per the [C5 no-dedicated-viewer Decision](open-questions.md)).
- Track F is independent of Track C (external assets) and the pending Track D/E branches; overlap
  with Track E (naval) is only incidental (shoreline destruction) and a future coordination point.
