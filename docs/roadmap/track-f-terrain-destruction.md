# Track F: Object Physics, Deformable Terrain & Destruction

> Part of the [Large-Scale RTS Roadmap](README.md). Applies the Bepu physics substrate to every
> physical `GameObject` through an explicit participation policy, then uses it for runtime terrain
> deformation and structural destruction. Non-physical helpers remain outside the solver. This is
> new fidelity beyond original SAGE and is not part of Track P's definition of done; execution
> dependencies still reuse Track P's harness and coverage. Split as its own track because it cuts
> across object lifecycle, collision, locomotion, terrain storage, rendering, combat, and
> save/replay (see the [scope Decision](open-questions.md)).
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
| F4. Physics engine integration | Establish the pluggable rigid-body seam and BepuPhysics 2.4 backend described by the [physics adoption plan](plans/physics-seam.md), runnable windowed and headless with benchmark coverage. This phase proves the substrate; it does not make the proof-of-concept adapter the default. | A falling/stacking scene runs through the seam windowed and headless within the same authored bounds, with `Timestep` cost reported at 100/1k/10k bodies by the benchmark harness. | In progress: seam + Bepu backend + `PhysicsStack` debris scene landed and validated headlessly (settle bounds, stability probe, ms/Timestep at 100/1k/10k bodies); windowed run and B1-harness benchmarks pending | Track P (P7), A1, B1 |
| F4a. Game-object physics adoption | Classify every instantiated `GameObject` as dynamic, kinematic, static, sensor, or no physics; centralize body lifecycle, compound shape/material/filter mapping, transform authority, and contact dispatch; migrate loose objects, ground units, aircraft, projectiles, structures/bridges/props, and watercraft before changing the default backend. Bepu owns solid contacts; `PartitionCellManager` retains gameplay queries and non-physical regions. | Cross-game definition audits have no unclassified instantiated objects; representative scenarios for every participation category pass; create/remove/teleport/contain/state transitions leave no stale bodies or duplicate collision events; Bepu is the default and legacy is explicit comparison only. | Not started | F4, P6b |
| F5. Physics-based structural destruction | Replace the canned `RubbleRiseUpdate`/`ToppleUpdate` model-swap on building death with rigid-body debris/collapse simulated through the F4a lifecycle (procedural or pre-authored fracture pieces), sized/shaped by the destroyed object's geometry and the destroying weapon. | A destroyed building transitions from its static body to simulated rigid-body debris that comes to rest, with no stale collider or single pre-baked rubble replacement, for at least one building type. | Not started | F4a, P6-3 |
| F6. Destruction-terrain interaction | Tie physics-based destruction (F5) and terrain deformation (F1) together so settled debris/craters affect pathing (blocked/impassable cells) and object placement. | A destroyed building's settled debris and any crater block pathing and prevent new construction on the same footprint until cleared. | Not started | F1, F3, F4a, F5, P6-3 |
| F7. Persistence & replay | Persist terrain deltas and all runtime object-physics state through `StatePersister`, excluding backend handles, and ensure ReplaySketch/P0 captures classification, transitions, deformation, contacts, and resting state for authored-bound validation. | A save/load round-trip reconstructs static bodies and preserves dynamic/kinematic mode, transform, velocities, sleep/constraint state, terrain deformation, and debris state; representative ReplaySketch scenarios resume within loose bounds without stale contacts. | Not started | F1, F3, F4a, F5 |
| F8. Authoring & validation tooling | Extend ReplaySketch with physics participation/contact assertions and collider inspection, plus terrain/debris preview through screenshot mode. Reuse the existing tool per the [C5 tooling Decision](open-questions.md). | ReplaySketch can inspect an object's resolved mode and collider, assert contact/settle bounds headlessly, and show deformed terrain and settled debris in screenshot mode. | Not started | F2, F4a, F6, F7 |

## Phase resources

A navigation index for each phase: design docs, architecture diagrams, and agent task cards.
Diagrams are required before any phase's first task card is marked `Ready`. F4/F4a and the
object-physics portions of F7/F8 use the physics adoption plan; other later F-phase designs are yet
to be written.

| Phase | Design doc | Diagrams | Agent cards |
|---|---|---|---|
| F1 | — | — | — |
| F2 | — | — | — |
| F3 | — | — | — |
| F4 | [Physics adoption plan](plans/physics-seam.md) | [Architecture diagrams](plans/physics-seam.md#architecture-diagrams) | [WI-PS-01 through WI-PS-03 index](plans/physics-seam.md#task-card-index) |
| F4a | [Physics adoption plan](plans/physics-seam.md) | [Architecture diagrams](plans/physics-seam.md#architecture-diagrams) | [WI-PS-04 through WI-PS-12 index](plans/physics-seam.md#task-card-index) |
| F5 | — | — | — |
| F6 | — | — | — |
| F7 | [Physics persistence](plans/physics-seam.md#persistence-and-replay) | [Logic tick](plans/physics-seam.md#logic-tick) | [WI-PS-13 index](plans/physics-seam.md#task-card-index) |
| F8 | [Physics validation](plans/physics-seam.md#validation) | [Architecture diagrams](plans/physics-seam.md#architecture-diagrams) | [WI-PS-14 index](plans/physics-seam.md#task-card-index) |

## Cross-track dependencies

Hard dependencies are in the phase table above; these are the coordination notes:

- F2 patch-mesh regeneration reuses [B5](track-b-scale.md) streaming/LOD rather than a parallel
  pipeline.
- F4a coordinates with [B2/B4](track-b-scale.md): Bepu replaces solid-pair broad-phase/response,
  while partition/pathfinding data remains authoritative for gameplay queries and routes.
- F4a supplies Bepu hull contacts and water support to [E2](track-e-naval-warfare.md); E1 remains
  authoritative for water passability.
- [B6](track-b-scale.md) starts its RTS implementation after F4a so each shard uses the same body
  state, contact authority, and lifecycle.
- F4a/F5/F6 coordinate with [B7 variable fidelity](track-b-scale.md): reduced-fidelity objects may
  sleep, become kinematic, or use aggregate collision, but they keep the same participation policy
  and promote to full Bepu state before decisive contact.
- F7 validates physics outcomes by invariants and loose authored bounds; cross-run divergence is
  acceptable, so no [A2](track-a-foundations.md) exact parity is required.
- F8 extends the [P0 ReplaySketch harness](deep-dives/p0-playable-baseline-harness.md); no new
  authoring tool (per the [C5 no-dedicated-viewer Decision](open-questions.md)).
- [H4](track-h-storage-formats.md) stores engine-level participation/material/filter overrides
  introduced by F4a without exposing Bepu-specific handles or types in content.
- Track F remains independent of Track C and Track D. W3D visual geometry is not implicitly treated
  as collision geometry; model-derived colliders require explicit collision data or a reviewed
  generated fallback.
