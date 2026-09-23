# Large-Scale RTS Simulation & External Asset Roadmap

> Living roadmap for an extensible framework for simulation-optimization experiments. Fixed aims:
> scalability and flexibility. Larger maps, distributed simulation, variable fidelity, and external
> assets remain framework experiments rather than terminal goals.
>
> Structure: linked documents; see [Document map](#document-map). Each phase carries a Status
> (`Not started` / `In progress` / `Done` / `Revised`). New deep dives belong under
> [`deep-dives/`](deep-dives/) and link from the owning phase.
>
> Audience: human overview and progress tracking.

---

## Vision

Purpose: a framework for integrating simulation-optimization experiments into the engine and
measuring their effects.

Fixed aims:

- Scalability - larger simulations, larger maps, headless/offline batch runs, distributed
  simulation, and reduced per-unit cost.
- Flexibility - pluggable techniques behind stable seams; pathfinders, fidelity policies,
  partitioning schemes, and model formats can be replaced without rewriting the engine.

Long-term goals:

1. Arbitrarily large simulations - scale toward thousands–millions of entities, larger maps,
   headless/offline batch simulation, and distributed simulation across processes/machines. The
   first committed waypoint is 10k units at interactive rate (~30 fps) on one machine (see the
   [near-term scale anchor Decision](open-questions.md)); larger figures remain directional.
2. External asset incorporation - support for models and animations authored outside the
   Westwood `.w3d` ecosystem through a format-agnostic import abstraction.
3. [First World War Western Front-scale simulation](deep-dives/western-front-scale-simulation.md) -
   a theater-scale, spatially explicit simulation combining
   [geospatial terrain](deep-dives/b4b-geospatial-map-generation.md),
   large/distributed/variable-fidelity simulation, and extensible gameplay content. This is a
   directional integration target; map extent, order of battle, temporal resolution, combat
   fidelity, and interactive-rate targets remain uncommitted.

Scope notes:

- These goals remain experiments rather than terminal objectives. New goals can be added later.
- The simulation domain remains pluggable; see the [Simulation Domain Model seam](track-a-foundations.md).
  RTS is the first domain, transport/graph is the first non-RTS validation target, and other
  models such as city builders and agent-based simulations extend the same `GameObject`/module
  system.
- Prerequisite: a playable baseline first. [Track P - Playable Baseline](track-p-playable-baseline.md)
  is the hard prerequisite. A full Generals/Zero Hour skirmish must run build → fight → win/lose
  before the headless host, benchmarks, and experiment seams have a correct match loop to scale
  and measure. Track A/B/C work remains gated on it.

### Guiding principles

- Framework first: optimization techniques are experiments behind stable seams with swappable
  implementations for pathfinding, fidelity policy, partitioning, model import, and storage
  layout. Each technique remains replaceable and independently measurable.
- Measure everything: benchmarking utilities are a deliverable, not ad-hoc scripts. Each
  technique is evaluated against [B1](track-b-scale.md) benchmarks and inspected with the
  [result replayer/visualizer](deep-dives/a5-visualization.md). The shared harness supports
  repeatable scenarios, multiple runs, variance/confidence reporting, discarded warmup,
  regression detection against stored baselines, machine-readable output, and cost/accuracy
  trade-offs.
- Authoring and inspection tools scale with the simulation. WorldBuilder and
  [ReplaySketch](deep-dives/a5-visualization.md) must handle the same entity counts, map sizes,
  and command volumes as the simulations they configure. WorldBuilder authoring scales under
  [B4a](track-b-scale.md), after the [A5-6](deep-dives/a5-visualization.md) baseline lands.
  ReplaySketch and the visualizer scale under [A5](deep-dives/a5-visualization.md) and
  [P0](track-p-playable-baseline.md), reusing the engine's streaming, LOD, instancing,
  and procedural-generation work from [B4a](deep-dives/b4a-larger-maps.md) and
  [B5](track-b-scale.md).
- Domain-agnostic where it is cheap. The simulation domain is a pluggable seam
  ([A4-1](deep-dives/a4-simulation-domain-model.md)), so the
  [FTRT headless host](deep-dives/a1-headless-host.md) and
  [distributed sim](deep-dives/b6-distributed-simulation.md) are not hard-wired to RTS. RTS is the
  reference domain rather than a baked-in assumption.
- Scale and extensibility win over original-engine fidelity when they conflict. Reading
  original SAGE data remains valuable, but it must not block the architectural changes scale
  requires. Recorded state rather than input re-simulation backs run analysis.
- Separate simulation from presentation cleanly. Headless and distributed scenarios need a
  logic core that runs without a renderer, audio, or window.
- Determinism is not a roadmap requirement. Validation uses per-run invariants, authored bounds,
  aggregate distributions, and performance statistics. Seeds remain optional experiment inputs.
- Incremental, always-shippable: each phase leaves the engine building and the existing games
  playable.
- Single-operator scope: the framework is a personal research tool for one operator for the
  foreseeable future. Tooling and run results target that operator rather than external users, so
  multi-user robustness, third-party reproducibility, and distribution hardening stay out of
  scope until that stance changes.

---

## Document map

### Audience guide

Audience map: two document classes. Use this to choose the first read.

| Audience | What to read | What to skip |
|---|---|---|
| Human (strategic overview, progress tracking, design intent) | `README.md`, `roadmap-tree.md`, `track-*.md`, `open-questions.md`, `implementation-checklist.md`, `deep-dives/` | `agent/`, `implementation/`, `agent/current-state.md`, `agent/MAINTAINING.md` |
| Agent (automated planning/execution, codebase context required) | `agent/`, `implementation/`, `agent/current-state.md`, `agent/MAINTAINING.md` | Track docs and deep dives provide needed context |

`seams.md` serves both audiences: the overview and diagrams are human-facing, while the
registry detail and file citations are agent context. A split into separate overview and registry
docs is planned.

Deep dives and implementation plans are human-facing. Deep dives keep code-grounding file:line
references in a marked appendix at the bottom. Implementation plans use a `---` divider between
human-readable problem/design/approach sections and agent-context file and test details.

### All documents

| Document | Audience | Contents |
|---|---|---|
| [`agent/current-state.md`](agent/current-state.md) | agent | Snapshot of the architecture this roadmap builds on (cited). |
| [`agent/MAINTAINING.md`](agent/MAINTAINING.md) | agent | Planning-agent protocol: the single checklist for propagating a plan change across all parallel docs (with a change-type matrix + consistency invariants). |
| [`roadmap-tree.md`](roadmap-tree.md) | human | Visual tree of the whole roadmap: tracks → phases → sub-phases hierarchy + the branching dependency flow. |
| [`seams.md`](seams.md) | human + agent | Definition of a "seam" and the maintained registry of all framework seams (structural + A4 experiment seams). Pending split into a human overview and an agent registry. |
| [`track-p-playable-baseline.md`](track-p-playable-baseline.md) | human | Prerequisite track: make the reference game (Generals/ZH) a fully playable end-to-end skirmish (setup, AI, economy, production, combat, win/lose). Gates Tracks A/B/C. |
| [`track-a-foundations.md`](track-a-foundations.md) | human | Shared prerequisites: headless host, statistical validation, sim/presentation seam, experiment framework, scenario authoring & visualization. |
| [`track-b-scale.md`](track-b-scale.md) | human | Scaling work: benchmarks, allocation cleanup, parallelism, spatial/pathing, rendering, distributed sim. |
| [`track-c-external-assets.md`](track-c-external-assets.md) | human | External model/animation import: abstraction, retargeting, materials, production glTF, EE2 NIF proof, tooling. |
| [`track-d-gameplay-expansion.md`](track-d-gameplay-expansion.md) | human | Gameplay content expansion beyond the built-in SAGE titles: tech-tree/prerequisite-graph generalization, era/age progression, external content ingestion (ModCollector + Track C), a reference non-SAGE scenario. Gated on Track P; D3 onward also gated on Track C/ModCollector. |
| [`track-e-naval-warfare.md`](track-e-naval-warfare.md) | human | Naval warfare: finishes water/ground pathfinding and adds ship combat/production against BFME2 content. E1/E3 depend on Track P; E2 also uses F4a Bepu hull contacts. |
| [`track-f-terrain-destruction.md`](track-f-terrain-destruction.md) | human | Object physics, deformable terrain & destruction: Bepu seam, explicit physics participation for every game object, runtime terrain deformation, structural destruction, persistence/replay, and authoring/validation tooling. |
| [`track-g-rendering-clarity.md`](track-g-rendering-clarity.md) | human | Rendering clarity & image quality: high-DPI/resolution correctness, graphics settings & fidelity presets, visual regression harness, MSAA, cutout/alpha-test clarity, gamma-correct rendering, texture filtering & mip quality, shadow quality tier, resolution-independent 2D/UI, optional realistic proportions. Not gated on Track P. |
| [`track-h-storage-formats.md`](track-h-storage-formats.md) | human | Map & unit storage formats: a new on-disk storage system for maps and object/unit template data. Format-agnostic interchange abstraction, a versioned successor to the `.map` format, a versioned successor to per-game INI object-definition storage, legacy migration/compatibility tooling, authoring/pipeline integration, round-trip regression. Persistence/schema concern, distinct from B3/B4a-5's runtime-performance focus. Gated on Track P. |
| [`plans/mod-collector.md`](plans/mod-collector.md) | human | Design for the standalone `OpenSage.Tools.ModCollector` CLI: downloads/stages community mod archives into `.big` files, a prerequisite for Track D phase D3. |
| [`plans/validation-ground-gap-closure.md`](plans/validation-ground-gap-closure.md) | human | Plan and `WI-VG-` card index for retiring the Unit Validation Ground's roster gaps (P9 follow-up): stale target-armor verdicts, host-side SpecialPower order construction, game-aware catalog, sandbox mechanics. |
| [`plans/tracer-rounds.md`](plans/tracer-rounds.md) | human | Plan for rendering bullet tracers through the existing `Tracer` FXList nugget: client-side `TracerManager`, directional additive quads, no projectile or logic-side involvement. |
| [`plans/physics-seam.md`](plans/physics-seam.md) | human + agent | Object physics adoption plan: BepuPhysics 2.4 seam, participation/lifecycle/contact policy, category matrix for all applicable game objects, default-backend cutover, and WI-PS card index. |
| [`open-questions.md`](open-questions.md) | human | Decision log: every item is now a settled Decision (rationale + affected phase). No questions currently open; add new ones here and convert to a Decision once settled. |
| [`implementation-checklist.md`](implementation-checklist.md) | human | Single granular change list: every concrete implementation change across the roadmap, ordered by dependency, checked off as it lands. |
| [`agent/`](agent/) | agent | Agent-focused companion: how a low-effort automated agent picks up small, bounded slices of the roadmap (task cards + workflow) without re-reasoning the whole design. |
| [`implementation/`](implementation/) | human | Per-sub-phase plans: problem, design rationale, and approach above the divider; file:line agent-context details below the divider. |
| [`deep-dives/`](deep-dives/) | human | Per-phase detailed designs. Code-grounding content is in a clearly marked appendix at the bottom of each doc; the sections above are readable without codebase context. |

### Deep dives

| Deep dive | Expands |
|---|---|
| [`deep-dives/p0-playable-baseline-harness.md`](deep-dives/p0-playable-baseline-harness.md) | Track P phase P0 - ReplaySketch arbitrary-command harness (open command model + headless assertion layer) and the agent validation playbook (MAUI testability seams + `ReplaySketch.Maui/AGENTS.md`). |
| [`deep-dives/p1-skirmish-setup.md`](deep-dives/p1-skirmish-setup.md) | Track P phase P1 - Skirmish setup & player relationships. |
| [`deep-dives/p2-p5-skirmish-ai.md`](deep-dives/p2-p5-skirmish-ai.md) | Track P phases P2 - Skirmish AI opponent (+ P5 construction/production mechanism). |
| [`deep-dives/p3-victory-defeat.md`](deep-dives/p3-victory-defeat.md) | Track P phase P3 - Victory & defeat conditions (queryable `GameOutcome`, all six match end states). |
| [`deep-dives/p4-p6-economy-production-combat.md`](deep-dives/p4-p6-economy-production-combat.md) | Track P phases P4 - Economy loop & P6 - Combat & command verification. |
| [`deep-dives/p6b-unit-contract-validation.md`](deep-dives/p6b-unit-contract-validation.md) | Track P phase P6b - Per-archetype unit-contract validation (infantry/vehicle/aircraft/builder/support/superweapon full contracts). |
| [`deep-dives/p8-implementation-delta-remediation.md`](deep-dives/p8-implementation-delta-remediation.md) | Track P phase P8 - Implementation delta remediation (closes named P3-P6 delta-audit gaps: data-driven victory config, sudden-death repeat-elimination fix, end-of-match UI/exit, real gather-cycle harness, real faction build trees, control-bar live dispatch, weapon-fire effects). |
| [`deep-dives/p9-unit-validation-ground.md`](deep-dives/p9-unit-validation-ground.md) | Track P phase P9 - Unit validation ground (whole-roster sandbox: direct-spawn every unit, order its primary action, measure duration in logic frames against a golden baseline; watchable windowed and asserted headlessly). |
| [`deep-dives/a1-headless-host.md`](deep-dives/a1-headless-host.md) | Track A phase A1 - Headless simulation host. |
| [`deep-dives/a4-pluggable-ai.md`](deep-dives/a4-pluggable-ai.md) | Track A phase A4 - Pluggable AI decision policy seam (rule-based reference implementation from P2, unblocking a neural-network policy trained via evolved seeded weight populations). |
| [`deep-dives/a4-decentralized-hierarchical-ai.md`](deep-dives/a4-decentralized-hierarchical-ai.md) | Track A phase A4 - Decentralized hierarchical AI (unit-level ANNs organized as general/commander/soldier ranks, generalizing the flat pluggable-AI policy seam). |
| [`deep-dives/a5-visualization.md`](deep-dives/a5-visualization.md) | Track A phase A5 - Scenario authoring & visualization (scenario setup, sweeps, result replayer/visualizer, divergence localization, authoring/visualization at scale). |
| [`deep-dives/b4a-larger-maps.md`](deep-dives/b4a-larger-maps.md) | Track B phase B4a - Larger maps (via WorldBuilder) (terrain scaling + large-population scenario authoring). |
| [`deep-dives/b4b-geospatial-map-generation.md`](deep-dives/b4b-geospatial-map-generation.md) | Track B phase B4b - Offline generation of editable maps from operator-supplied Google Earth or Google Earth Engine geospatial exports. |
| [`deep-dives/b6-distributed-simulation.md`](deep-dives/b6-distributed-simulation.md) | Track B phase B6 - Distributed simulation (spatial sharding). |
| [`deep-dives/b7-variable-fidelity.md`](deep-dives/b7-variable-fidelity.md) | Track B phase B7 - Variable simulation fidelity (scaling toolkit). |
| [`deep-dives/western-front-scale-simulation.md`](deep-dives/western-front-scale-simulation.md) | Long-term goal - experiment charter and dependency map for a Western Front-scale First World War simulation. |
| [`deep-dives/c1-import-abstraction.md`](deep-dives/c1-import-abstraction.md) | Track C phase C1 - CPU-only model and animation import contracts, W3D reference adapters, runtime materialization, and parity. |
| [`deep-dives/c2-skeleton-animation-retargeting.md`](deep-dives/c2-skeleton-animation-retargeting.md) | Track C phase C2 - Direct-index and exact-name animation-channel binding to runtime model hierarchies. |
| [`deep-dives/c3-material-texture-mapping.md`](deep-dives/c3-material-texture-mapping.md) | Track C phase C3 - Format-neutral core metallic-roughness materials, texture bindings, and engine PBR materialization. |
| [`deep-dives/c4-gltf-importer.md`](deep-dives/c4-gltf-importer.md) | Track C phase C4 - Strict glTF 2.0 static, skin, animation, coordinate, and core PBR conversion through C1-C3. |
| [`deep-dives/ee2-gamebryo-model-storage.md`](deep-dives/ee2-gamebryo-model-storage.md) | Track C phase C4a - Empire Earth II Gamebryo corpus survey and bounded NIF 10.1 model-loading proof of concept. |
| [`deep-dives/c4c-ee2-rigid-animation.md`](deep-dives/c4c-ee2-rigid-animation.md) | Track C phase C4c - Empire Earth II NIF 10.1 KF decoding, node-hierarchy bones, Euler/Hermite resampling, and C2 playback for the proof models. |
| [`deep-dives/c4d-ee2-skinned-animation.md`](deep-dives/c4d-ee2-skinned-animation.md) | Track C phase C4d - Empire Earth II `NiSkinInstance` / `NiSkinData` import, four-influence C1 and runtime skinning, and the NIF rotation-convention fix, proven on the British Druid. |
| [`deep-dives/c4e-ee2-property-animation.md`](deep-dives/c4e-ee2-property-animation.md) | Track C phase C4e - Empire Earth II visibility, UV, material color, and alpha controllers with per-instance material state. |
| [`deep-dives/c4f-ee2-scale-animation.md`](deep-dives/c4f-ee2-scale-animation.md) | Track C phase C4f - Empire Earth II uniform scale keys, bone rest scale, and scale channels through C1 and the runtime. |
| [`deep-dives/c4g-ee2-morph-animation.md`](deep-dives/c4g-ee2-morph-animation.md) | Track C phase C4g - Empire Earth II morph-target animation implementing the shared C6 morph runtime. |
| [`deep-dives/c4h-ee2-particle-effects.md`](deep-dives/c4h-ee2-particle-effects.md) | Track C phase C4h - Empire Earth II legacy `NiParticles` effects mapped onto the existing particle runtime. |
| [`deep-dives/c4i-ee2-legacy-kf-formats.md`](deep-dives/c4i-ee2-legacy-kf-formats.md) | Track C phase C4i - Empire Earth II NIF 10.0.1.0 KF support and NetImmerse 4.2.2.0 rejection. |
| [`deep-dives/c5-external-model-tooling.md`](deep-dives/c5-external-model-tooling.md) | Track C phase C5 - ReplaySketch external-model inspection and TestHarness preview/report protocol. |
| [`deep-dives/c6-extended-model-features.md`](deep-dives/c6-extended-model-features.md) | Track C phase C6 - Request-scoped morph-target and advanced-PBR experiment gates. |
| [`deep-dives/d1-d2-tech-tree-era-progression.md`](deep-dives/d1-d2-tech-tree-era-progression.md) | Track D phases D1/D2 - Tech-tree/prerequisite-graph generalization and era/age progression. |
| [`deep-dives/e1-e2-naval-warfare.md`](deep-dives/e1-e2-naval-warfare.md) | Track E phases E1/E2 - Water/ground terrain passability and ship combat. |
| [`deep-dives/g1-high-dpi-resolution.md`](deep-dives/g1-high-dpi-resolution.md) | Track G phase G1 - High-DPI drawable & resolution correctness (logical points versus physical pixels across window, panel, viewport and GUI). |

---

## Tracks at a glance

Track layout: a prerequisite Playable Baseline track, a shared Foundations track, and parallel
Scale, External Assets, Gameplay Content, Naval Warfare, Object Physics/Terrain/Destruction, and
Rendering Clarity tracks. Phase order is approximate; cross-track dependencies appear in each
track document.

- [Track P - Playable Baseline](track-p-playable-baseline.md) (prerequisite): P0 ReplaySketch
  arbitrary-command harness · P1 skirmish setup & player relationships · P2 skirmish AI opponent ·
  P3 victory/defeat conditions · P4 economy loop · P5 construction & production · P6 combat &
  command · P6b unit-contract validation · P7 end-to-end skirmish validation · P8 implementation
  delta remediation · P9 unit validation ground. Gates all other tracks.
- [Track A - Foundations](track-a-foundations.md): A1 headless/FTRT host · A2 statistical
  validation · A3 simulation/presentation seam · A4 experiment framework seams & harness
  (incl. the Simulation Domain Model seam - RTS, city-builder, transport, …) · A5 scenario
  authoring & visualization (incl. authoring/visualization at scale - the ReplaySketch tooling
  scales with the sim).
- [Track B - Scale](track-b-scale.md): B1 profiling & benchmarking utilities · B2 allocation cleanup · B3
  parallel/data-oriented logic · B4 spatial & pathfinding scaling · B4a larger maps (+
  large-population scenario authoring in WorldBuilder) · B4b geospatial map generation from Google
  Earth KML/KMZ plus operator-authorized elevation and imagery data · B5 rendering at scale · B6
  distributed simulation · B7 variable simulation fidelity.
- [Track C - External Models & Animations](track-c-external-assets.md): C1 import
  abstraction · C2 skeleton/animation retargeting · C3 material mapping · C4 first production
  format (glTF static + skinned) · C4a EE2 NIF 10.1 static-model proof · C4b EE2 cooked
  textures · C4c EE2 rigid KF animation · C4d EE2 skinned character animation · C4e EE2
  property animation · C4f EE2 scale animation · C4g EE2 morph animation · C4h EE2 particle
  effects · C4i EE2 legacy KF formats · C5
  authoring/validation tooling · C6 extended features (deferred: morph targets, advanced PBR).
- [Track D - Gameplay Content Expansion](track-d-gameplay-expansion.md): D1 tech-tree/
  prerequisite-graph generalization · D2 era/age progression mechanic · D3 external content
  ingestion (ModCollector + Track C) · D4 reference non-SAGE scenario. Depends on Track P
  (D1/D2) and Track C/ModCollector (D3/D4).
- [Track E - Naval Warfare](track-e-naval-warfare.md): E1 water/ground terrain passability ·
  E2 ship combat · E3 naval production (`PORT`) · E4 reference naval scenario. Depends on Track
  P; E2 also depends on F4a's shared hull/contact path.
- [Track F - Object Physics, Deformable Terrain & Destruction](track-f-terrain-destruction.md): F1 terrain
  deformation data model (chunked-heightmap deltas) · F2 runtime mesh/rendering update · F3
  weapon/combat-driven terrain deformation · F4 physics engine integration (pluggable rigid-body
  seam) · F4a Bepu adoption across dynamic/kinematic/static/sensor objects · F5 physics-based
  structural destruction (replaces canned rubble/damage-state model swap) · F6 destruction-terrain
  interaction (debris/craters block pathing & placement) · F7 persistence/replay · F8
  authoring/validation tooling.
- [Track G - Rendering Clarity & Image Quality](track-g-rendering-clarity.md): G1 high-DPI
  drawable & resolution correctness · G2 graphics settings & fidelity presets
  (`Faithful`/`Enhanced`) · G3 visual regression harness · G4 multisample antialiasing · G5
  cutout & alpha-test clarity (the screen-door fix) · G6 gamma-correct rendering · G7 texture
  filtering & mip quality · G8 shadow quality tier · G9 resolution-independent 2D & UI ·
  G10 realistic proportions (optional scale-correct aircraft presentation). Not gated
  on Track P.
- [Track H - Map & Unit Storage Formats](track-h-storage-formats.md): H1 format requirements &
  current-state audit · H2 format-agnostic interchange abstraction · H3 new map storage format ·
  H4 new object/unit-definition storage format · H5 legacy migration & compatibility tooling · H6
  authoring & pipeline integration (WorldBuilder, ModCollector) · H7 validation & round-trip
  regression. A persistence/schema-design track, distinct from B3/B4a-5's runtime-performance
  focus. Depends on Track P only.

### Cross-track dependencies

- Track P gates everything. [A1 (headless host)](track-a-foundations.md) depends on
  P7 - a validated, terminating skirmish loop is the headless host's unit of work; A2/A4 and
  all of Tracks B/C/F inherit that prerequisite.
- B5 (instanced rendering) should account for Track C models so instancing is not
  W3D-specific.
- C1 and A3 both touch the presentation seam - coordinate so the import abstraction
  feeds the same runtime `Model`/`ModelInstance` types that the rendering refactor expects.
- Track D (D1/D2) depends only on Track P; D3/D4 additionally depend on Track C (C1) and the
  standalone [ModCollector tool](plans/mod-collector.md).
- Track E depends on Track P; E2 additionally consumes F4a's Bepu hull contacts and water-support
  body mode. It still needs neither Track C nor ModCollector. B4a should coordinate terrain
  streaming with E1's water-depth data once both land.
- Track F's F1 (terrain deformation data model) depends on B4a-5 (chunked terrain storage) and
  F2 (rendering update) coordinates with B5 - deformation should build on the chunked
  representation and shared rendering-at-scale infrastructure rather than a parallel one.
- Track F's F4a classifies every instantiated object but creates Bepu bodies only for physical
  roles. It coordinates with B2/B4 over solid-contact ownership, gates B6's RTS shards and B7's
  reduced-fidelity bodies, supplies E2 ship physics, and defines H4's engine-level physics
  metadata. F5/F6 then use the same lifecycle for structural destruction and terrain obstruction.
- Track G is the only track not gated on Track P: it changes presentation only and no simulation
  behaviour. It coordinates with B5 (rendering at scale) over pipeline-description plumbing and
  with A3 (sim/presentation seam) over the pixel-versus-logical-point split, and its G3 golden
  frames guard Track F's terrain-rendering changes (F2/F8).
- Track H depends only on Track P. Its H3 (new map storage format) reserves a terrain section for
  [B4a-5's chunked heightmap representation](deep-dives/b4a-larger-maps.md) rather than competing
  with it: B4a-5 owns the runtime chunking/streaming model, H3 owns the on-disk container and
  versioning. B4b consumes both through H6's WorldBuilder integration as the output path for
  generated geospatial maps. H2's
  format-agnostic interchange abstraction mirrors [C1](track-c-external-assets.md)'s
  import-abstraction pattern applied to map/object-definition data instead of models/animations.
  H4/H6 feed Track D's D3 external content ingestion and the standalone [ModCollector
  tool](plans/mod-collector.md) once the new object-definition format exists.

---

## Progress

Progress counts follow the [implementation checklist](implementation-checklist.md). Updates occur when checklist items are checked off.

### Track P - Playable Baseline

| Phase | Progress | Items |
|---|---|---|
| P0 - ReplaySketch harness | `███░░░░░░░` | 4 / 15 |
| P1 - Skirmish setup | `██████░░░░` | 5 / 8 |
| P2 - Skirmish AI | `██████████` | 7 / 7 |
| P3 - Victory & defeat | `██████████` | 9 / 9 |
| P4 - Economy loop | `██████████` | 5 / 5 |
| P5 - Construction & production | `██████████` | 5 / 5 |
| P6 - Combat & command | `████████░░` | 4 / 5 |
| P6b - Unit-contract validation | `██████████` | 8 / 8 |
| P6c - Special-power implementation | `██████████` | 4 / 4 |
| P7 - End-to-end validation | `██████████` | 8 / 8 |
| P8 - Implementation delta remediation | `░░░░░░░░░░` | 0 / 7 |
| P9 - Unit validation ground | `░░░░░░░░░░` | 0 / 8 |
| Track P total | `██████▌░░░` | 51 / 87 |

### Tracks A / B / C (gated on Track P)

| Track | Progress | Items |
|---|---|---|
| Track A - Foundations | `███████░░░` | 12 / 19 |
| Track B - Scale | `░░░░░░░░░░` | 0 / 13 |
| Track C - External Assets | `█████░░░░░` | 7 / 15 |

### Track D - Gameplay Content Expansion (gated on Track P; D3/D4 also on Track C/ModCollector)

| Phase | Progress | Items |
|---|---|---|
| D1 - Tech tree / prerequisite graph | `░░░░░░░░░░` | 0 / 1 |
| D2 - Era/age progression | `░░░░░░░░░░` | 0 / 1 |
| D3 - External content ingestion | `░░░░░░░░░░` | 0 / 1 |
| D4 - Reference non-SAGE scenario | `░░░░░░░░░░` | 0 / 1 |
| Track D total | `░░░░░░░░░░` | 0 / 4 |

### Track E - Naval Warfare (gated on Track P; E2 also on F4a)

| Phase | Progress | Items |
|---|---|---|
| E1 - Water/ground terrain passability | `░░░░░░░░░░` | 0 / 1 |
| E2 - Ship combat | `░░░░░░░░░░` | 0 / 1 |
| E3 - Naval production | `░░░░░░░░░░` | 0 / 1 |
| E4 - Reference naval scenario | `░░░░░░░░░░` | 0 / 1 |
| Track E total | `░░░░░░░░░░` | 0 / 4 |

### Track F - Object Physics, Deformable Terrain & Destruction (gated on Track P)

| Phase | Progress | Items |
|---|---|---|
| F1 - Terrain deformation data model | `░░░░░░░░░░` | 0 / 1 |
| F2 - Runtime mesh & rendering update | `░░░░░░░░░░` | 0 / 1 |
| F3 - Weapon-driven terrain deformation | `░░░░░░░░░░` | 0 / 1 |
| F4 - Physics engine integration | `░░░░░░░░░░` | 0 / 1 |
| F4a - Game-object physics adoption | `░░░░░░░░░░` | 0 / 10 |
| F5 - Physics-based structural destruction | `░░░░░░░░░░` | 0 / 1 |
| F6 - Destruction-terrain interaction | `░░░░░░░░░░` | 0 / 1 |
| F7 - Persistence & replay | `░░░░░░░░░░` | 0 / 1 |
| F8 - Authoring & validation tooling | `░░░░░░░░░░` | 0 / 1 |
| Track F total | `░░░░░░░░░░` | 0 / 18 |

### Track G - Rendering Clarity & Image Quality (not gated on Track P)

| Phase | Progress | Items |
|---|---|---|
| G1 - High-DPI & resolution correctness | `░░░░░░░░░░` | 0 / 4 |
| G2 - Graphics settings & fidelity presets | `░░░░░░░░░░` | 0 / 1 |
| G3 - Visual regression harness | `░░░░░░░░░░` | 0 / 1 |
| G4 - Multisample antialiasing | `░░░░░░░░░░` | 0 / 1 |
| G5 - Cutout & alpha-test clarity | `░░░░░░░░░░` | 0 / 1 |
| G6 - Gamma-correct rendering | `░░░░░░░░░░` | 0 / 1 |
| G7 - Texture filtering & mip quality | `░░░░░░░░░░` | 0 / 1 |
| G8 - Shadow quality tier | `░░░░░░░░░░` | 0 / 1 |
| G9 - Resolution-independent 2D & UI | `░░░░░░░░░░` | 0 / 2 |
| G10 - Realistic proportions | `░░░░░░░░░░` | 0 / 3 |
| Track G total | `░░░░░░░░░░` | 0 / 16 |

### Track H - Map & Unit Storage Formats (gated on Track P only)

| Phase | Progress | Items |
|---|---|---|
| H1 - Format requirements & current-state audit | `░░░░░░░░░░` | 0 / 1 |
| H2 - Format-agnostic interchange abstraction | `░░░░░░░░░░` | 0 / 1 |
| H3 - New map storage format | `░░░░░░░░░░` | 0 / 1 |
| H4 - New object/unit-definition storage format | `░░░░░░░░░░` | 0 / 1 |
| H5 - Legacy migration & compatibility tooling | `░░░░░░░░░░` | 0 / 1 |
| H6 - Authoring & pipeline integration | `░░░░░░░░░░` | 0 / 1 |
| H7 - Validation & round-trip regression | `░░░░░░░░░░` | 0 / 1 |
| Track H total | `░░░░░░░░░░` | 0 / 7 |

---



> Plan maintenance: follow [`agent/MAINTAINING.md`](agent/MAINTAINING.md). That document is the
> planning-agent protocol for propagating a change across the parallel roadmap docs. The bullets
> below are a summary.

- Update a phase's Status in its track document when the phase changes state.
- Keep the [Progress](#progress) tables in sync with checklist done/total counts and bars.
- Keep the [implementation checklist](implementation-checklist.md) in sync as granular changes land,
  and add finer items as a phase gains detail.
- When a phase gets a detailed design, add a doc under [`deep-dives/`](deep-dives/) and link it
  from the phase row and the [Deep dives](#deep-dives) table.
- Refresh [`agent/current-state.md`](agent/current-state.md) when it drifts from the code; it
  underpins each phase's assumptions.
- Record significant decisions in [`open-questions.md`](open-questions.md) and update the affected
  phase.
- Keep this README's tables in sync when documents or deep dives are added or removed.
- Update the [visual roadmap tree](roadmap-tree.md) whenever a phase, sub-phase, or dependency is
  added, removed, or renamed.
- Update the [seam registry](seams.md) whenever a phase introduces a new pluggable seam or a new
  implementation behind an existing seam.
- When a phase is ready for cheap or automated agents, decompose it into task cards in
  [`agent/`](agent/) and keep card Status aligned with phase Status.
