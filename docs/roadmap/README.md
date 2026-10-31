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
   large/distributed/variable-fidelity simulation, and extensible gameplay content. Staged as
   [Track W](track-w-western-front.md): Sedan 1870 is the first real battle and the large-scale
   proof of concept, then a Somme 1916 sector; performance and interactive-rate targets remain
   uncommitted until W2 sets the first error envelope.
4. [1000 vs 1000 mixed-roster battle](deep-dives/mass-battle-1k.md) - an AI versus AI battle of
   1000 Command & Conquer Generals and Empire Earth II units per side on a large community or
   authored map, run headless in batches and watchable live in real time. Staged as
   [Track M](track-m-mass-battle.md); independent of Track W.

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
  requires.
- Separate simulation from presentation cleanly. Headless and distributed scenarios need a
  logic core that runs without a renderer, audio, or window.
- Determinism is not a roadmap requirement. Validation uses per-run invariants, authored bounds,
  aggregate distributions, and performance statistics. Seeds remain optional experiment inputs.
- Disk use beats responsiveness for run analysis: large runs record operational state and sparse
  checkpoints, and rebuild granular frames by re-simulation
  ([A1-6](deep-dives/a1-6-operational-recording.md)). Rebuilt frames are plausible, not
  authoritative.
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
| [`track-b-scale.md`](track-b-scale.md) | human | Scaling work: benchmarks, allocation cleanup, parallelism, spatial/pathing, larger maps, rendering, distributed sim, variable fidelity. |
| [`track-c-external-assets.md`](track-c-external-assets.md) | human | External model/animation import: abstraction, retargeting, materials, production glTF, EE2 NIF proof, tooling, StarCraft II, BFME-family W3D, Total War: THREE KINGDOMS, AoE3 DE, Rise of Legends. |
| [`track-d-gameplay-expansion.md`](track-d-gameplay-expansion.md) | human | Gameplay content expansion beyond the built-in SAGE titles: tech-tree/prerequisite-graph generalization, era/age progression, external content ingestion (ModCollector + Track C), a reference non-SAGE scenario. Gated on Track P; D3 onward also gated on Track C/ModCollector. |
| [`track-e-naval-warfare.md`](track-e-naval-warfare.md) | human | Naval warfare: finishes water/ground pathfinding and adds ship combat/production against BFME2 content. E1/E3 depend on Track P; E2 also uses F4a Bepu hull contacts. |
| [`track-f-terrain-destruction.md`](track-f-terrain-destruction.md) | human | Object physics, deformable terrain & destruction: Bepu seam, explicit physics participation for every game object, runtime terrain deformation, structural destruction, persistence/replay, and authoring/validation tooling. |
| [`track-g-rendering-clarity.md`](track-g-rendering-clarity.md) | human | Rendering clarity & image quality: high-DPI/resolution correctness, graphics settings & fidelity presets, visual regression harness, MSAA, cutout/alpha-test clarity, gamma-correct rendering, texture filtering & mip quality, shadow quality tier, resolution-independent 2D/UI, optional realistic proportions. Not gated on Track P. |
| [`track-h-storage-formats.md`](track-h-storage-formats.md) | human | Shared unit-content importing (H2a, before M2), map interchange, versioned map/unit storage, migration, authoring integration, and round-trip validation. Distinct from B3/B4a-5 runtime storage. H2a depends on P9/C4d; the storage foundation is gated on Track P. |
| [`track-w-western-front.md`](track-w-western-front.md) | human | Western Front simulation: charter fixture, aggregate sector model, fidelity promotion and reconciliation, geospatial sector, command and logistics experiments, distributed regions, and a long-run campaign. Built as a domain through the A4-1 seam and A4-4 state projection. |
| [`track-m-mass-battle.md`](track-m-mass-battle.md) | human | Mixed-roster mass battle: capacity probe, battle map, EE2 combat units, roster normalization, army-scale AI, and the 1000 vs 1000 battle in batch, replay, and live modes. |
| [`track-n-pathfinding-performance.md`](track-n-pathfinding-performance.md) | human | Multi-unit pathfinding performance and deadlocks: path cost baseline, search core efficiency, shared group requests, gated shared-destination fields, off-thread order paths, scale verification, stall attribution, path liveness, blocked-unit resolution, destination crowding, passage queueing, and layered navigation (strategic sectors, corridor main paths, navigation groups, steering). |
| [`track-r-real-world-terrain.md`](track-r-real-world-terrain.md) | human | Real-world terrain: operator-supplied Google Earth KML/KMZ plus georeferenced elevation and imagery rasters to standalone payloads, dev preview maps, H3 packages, and WorldBuilder import. Formerly Track B phase B4b. |
| [`track-x-fixes-tweaks.md`](track-x-fixes-tweaks.md) | human | Open-ended miscellaneous fixes and tweaks, each with a bounded milestone. |
| [`plans/mod-collector.md`](plans/mod-collector.md) | human | Design for the standalone `Panopticon.Tools.ModCollector` CLI: downloads/stages community mod archives into `.big` files, a prerequisite for Track D phase D3. |
| [`plans/validation-ground-gap-closure.md`](plans/validation-ground-gap-closure.md) | human | Plan and `WI-VG-` card index for retiring the Unit Validation Ground's roster gaps (P9 follow-up): stale target-armor verdicts, host-side SpecialPower order construction, game-aware catalog, sandbox mechanics. |
| [`plans/tracer-rounds.md`](plans/tracer-rounds.md) | human | Plan for rendering bullet tracers through the existing `Tracer` FXList nugget: client-side `TracerManager`, directional additive quads, no projectile or logic-side involvement. |
| [`plans/physics-seam.md`](plans/physics-seam.md) | human + agent | Object physics adoption plan: BepuPhysics 2.4 seam, participation/lifecycle/contact policy, category matrix for all applicable game objects, default-backend cutover, and WI-PS card index. |
| [`plans/argument-validation.md`](plans/argument-validation.md) | human + agent | Solution-wide refactor to single-line argument validation: BCL throw helpers plus a shared `Ensure` class, `ArgumentOutOfRangeException` for range checks, domain messages kept; `WI-AV-` card index. |
| [`plans/known-test-failures.md`](plans/known-test-failures.md) | human + agent | Remediation sequence and acceptance gates for eight recurring non-interactive test failures. |
| [`plans/m5-closeout.md`](plans/m5-closeout.md) | human + agent | Execution sequence for the incomplete phases needed to close Track M through M5, including the conditional B2/B3 decision and the A5 completion sequence. |
| [`plans/macos-battle-artifacts.md`](plans/macos-battle-artifacts.md) | human + agent | G6/P6 corrective follow-ups: global particle-opacity clamping and jet crash lifecycle, with evidence, capture contracts, and card dependencies. |
| [`open-questions.md`](open-questions.md) | human | Decision log: every item is now a settled Decision (rationale + affected phase). No questions currently open; add new ones here and convert to a Decision once settled. |
| [`implementation-checklist.md`](implementation-checklist.md) | human | Single granular change list: every concrete implementation change across the roadmap, ordered by dependency, checked off as it lands. |
| [`agent/`](agent/) | agent | Agent-focused companion: how a low-effort automated agent picks up small, bounded slices of the roadmap (task cards + workflow) without re-reasoning the whole design. |
| [`implementation/`](implementation/) | human | Per-sub-phase plans: problem, design rationale, and approach above the divider; file:line agent-context details below the divider. |
| [`deep-dives/`](deep-dives/) | human | Per-phase detailed designs. Code-grounding content is in a clearly marked appendix at the bottom of each doc; the sections above are readable without codebase context. |

### Deep dives

| Deep dive | Expands |
|---|---|
| [`deep-dives/h2a-unit-content-import.md`](deep-dives/h2a-unit-content-import.md) | H2a design scaffold and WI-H2a-01..19: shared unit-content importing and the Generals/EE2 proof before M2. |
| [`deep-dives/h2b-metric-core.md`](deep-dives/h2b-metric-core.md) | H2b design scaffold and WI-H2b-01..18: post-H2a conversion of the engine's internal length unit to meters. |
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
| [`deep-dives/a1-6-operational-recording.md`](deep-dives/a1-6-operational-recording.md) | Track A sub-phase A1-6 - Operational recording: operational stream, sparse engine checkpoints (every 6 campaign hours), and re-simulation of granular frames from the nearest checkpoint. |
| [`deep-dives/a4-4-domain-state-projection.md`](deep-dives/a4-4-domain-state-projection.md) | Track A checklist item A4-4 - Optional domain state projection: versioned schema, keyed multi-field records, domain-state artifact, and schema-driven conservation check. |
| [`deep-dives/a4-experiment-harness.md`](deep-dives/a4-experiment-harness.md) | Track A phase A4 - Experiment harness: typed experiment definitions, implementation registry, per-cell B1 execution, and the A5-consumable comparison report (WI-A4-37..41). |
| [`deep-dives/a5-visualization.md`](deep-dives/a5-visualization.md) | Track A phase A5 - Scenario authoring & visualization (scenario setup, sweeps, result replayer/visualizer, divergence localization, authoring/visualization at scale). |
| [`deep-dives/b1-benchmarking.md`](deep-dives/b1-benchmarking.md) | Track B phase B1 - Macro benchmark harness, BenchmarkDotNet micro benchmarks, A2-compatible JSON records, bootstrap confidence intervals, and regression comparison. |
| [`deep-dives/b4-spatial-pathfinding-scaling.md`](deep-dives/b4-spatial-pathfinding-scaling.md) | Track B phase B4 - Spatial and pathfinding scaling (partition hot-path budget, group movement, async path requests, gated hierarchical pathfinding). |
| [`deep-dives/b4a-larger-maps.md`](deep-dives/b4a-larger-maps.md) | Track B phase B4a - Larger maps (via WorldBuilder) (terrain scaling + large-population scenario authoring). |
| [`deep-dives/b4b-geospatial-map-generation.md`](deep-dives/b4b-geospatial-map-generation.md) | Track R phases R1-R5 (formerly B4b) - Offline generation of editable maps from operator-supplied Google Earth or Google Earth Engine geospatial exports. |
| [`deep-dives/b5-rendering-at-scale.md`](deep-dives/b5-rendering-at-scale.md) | Track B phase B5 - Rendering at scale: source-agnostic GPU instancing, distance LOD/culling, render-bucket/shadow submit improvements, and the render-scale probe. |
| [`deep-dives/b6-distributed-simulation.md`](deep-dives/b6-distributed-simulation.md) | Track B phase B6 - Distributed simulation (spatial sharding). |
| [`deep-dives/b7-variable-fidelity.md`](deep-dives/b7-variable-fidelity.md) | Track B phase B7 - Variable simulation fidelity (fidelity registry, group aggregation contract, visualization and error reporting). |
| [`deep-dives/western-front-scale-simulation.md`](deep-dives/western-front-scale-simulation.md) | Long-term goal - experiment charter and dependency map for a Western Front-scale First World War simulation; source of Track W. |
| [`deep-dives/w0-charter-fixture.md`](deep-dives/w0-charter-fixture.md) | Track W phase W0 - Stable keys, state schema, synthetic topology and scenario, multi-rate theater clock, and conservation ledger. |
| [`deep-dives/w1-aggregate-sector.md`](deep-dives/w1-aggregate-sector.md) | Track W phase W1 - Command and information, movement, logistics, consumption, and platoon-level stochastic and mean-field combat at operational cadence. |
| [`deep-dives/w2-fidelity-promotion.md`](deep-dives/w2-fidelity-promotion.md) | Track W phase W2 - Activation of every contested region, promotion to soldier objects with A4-3 groupings, tactical engagement, demotion with exact reconciliation, and the all-tactical reference comparison. |
| [`deep-dives/w3-w6-integration-experiments.md`](deep-dives/w3-w6-integration-experiments.md) | Track W phases W3a-W6 - Overview of the Sedan battle, the Somme sector, command and logistics sweeps, distributed regions, and the long-run campaign. |
| [`deep-dives/w3a-sedan-1870.md`](deep-dives/w3a-sedan-1870.md) | Track W phase W3a - Battle of Sedan 1870 at true scale: historical order of battle and orders timeline, period weapons, tactical artillery and cavalry, surrender and capitulation, the generic geospatial topology builder, and a 320,000-soldier all-tactical reference. |
| [`deep-dives/w3b-somme-sector.md`](deep-dives/w3b-somme-sector.md) | Track W phase W3b - Somme 1916 sector at true scale in UTM 31N, front-line bands on the W3a topology builder, and W1/W2 evidence on real geography. |
| [`deep-dives/w4-command-logistics-experiment.md`](deep-dives/w4-command-logistics-experiment.md) | Track W phase W4 - 216-cell command and logistics matrix, information audit, and sensitivity report. |
| [`deep-dives/w5-distributed-regional-simulation.md`](deep-dives/w5-distributed-regional-simulation.md) | Track W phase W5 - Front-strip shards over B6, lockstep coordination, and cross-boundary handoff. |
| [`deep-dives/w6-long-run-campaign.md`](deep-dives/w6-long-run-campaign.md) | Track W phase W6 - Long-run campaign file, domain snapshot plus host save, replay indexing, and approximation error reporting. |
| [`deep-dives/mass-battle-1k.md`](deep-dives/mass-battle-1k.md) | Long-term goal and Track M phases M0-M5 - Viewing model, map requirements and sourcing, EE2 DDF translation and roster normalization, army-scale AI, and scale dependencies. |
| [`deep-dives/n-multi-unit-pathfinding.md`](deep-dives/n-multi-unit-pathfinding.md) | Track N phases N0-N5 - Path cost evidence, budgets, search core, follower landing, group selection moves, gated goal fields, async order paths. |
| [`deep-dives/n-navigation-deadlocks.md`](deep-dives/n-navigation-deadlocks.md) | Track N phases N6-N10 - Deadlock evidence, stall attribution, path liveness, blocked-unit resolution, destination crowding. |
| [`deep-dives/n-confined-passage-queueing.md`](deep-dives/n-confined-passage-queueing.md) | Track N phases N11-N14 - Passage detection from route cross-sections, ad hoc passage queues with shared lane paths and unit order, opposing flow, squad entry. |
| [`deep-dives/n-layered-navigation.md`](deep-dives/n-layered-navigation.md) | Track N phases N15-N20 - Layer contract, sector-and-portal strategic graph, corridor main paths, persistent navigation groups, reciprocal steering. |
| [`deep-dives/x1-imported-unit-hit-feedback.md`](deep-dives/x1-imported-unit-hit-feedback.md) | X1: explicit imported-vehicle target effects and readable mixed-source duel evidence. |
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
| [`deep-dives/sc2-casc-m3-storage.md`](deep-dives/sc2-casc-m3-storage.md) | Track C phases C7a/C7b - StarCraft II CASC storage reader and static M3 models with DDS materials on C3 PBR. |
| [`deep-dives/c7c-sc2-skinned-animation.md`](deep-dives/c7c-sc2-skinned-animation.md) | Track C phase C7c - StarCraft II M3 skinning, embedded sequences, `.m3a` clips, per-axis scale, and absolute clip composition. |
| [`deep-dives/c8-sage-family-w3d.md`](deep-dives/c8-sage-family-w3d.md) | Track C phases C8a-C8c - BFME, BFME2, and RotWK W3D corpus, shader-material meshes, dual-bone skins, and animation through the native W3D path. |
| [`deep-dives/c9-total-war-3k-models.md`](deep-dives/c9-total-war-3k-models.md) | Track C phases C9a-C9d - Total War: THREE KINGDOMS packs, RMV2 parts, version 7 animation, and composite soldiers. |
| [`deep-dives/c10-aoe3de-granny-models.md`](deep-dives/c10-aoe3de-granny-models.md) | Track C phases C10a-C10d - Age of Empires III: DE `.bar` archives, XMB, DDT, and Granny `.gr2` models and animation. |
| [`deep-dives/c11-rise-of-legends-models.md`](deep-dives/c11-rise-of-legends-models.md) | Track C phases C11a-C11c - Rise of Legends WAR-BUILDER archives, BH3 models, and BHA animation. |
| [`deep-dives/c12-total-war-shogun2-models.md`](deep-dives/c12-total-war-shogun2-models.md) | Track C phases C12a-C12c - Total War: SHOGUN 2 `PFHs` packs with LZ4, RMV2 v0-v2 and `VMPF` parts, version 1 animation, and `.unit_variant` soldiers. |
| [`deep-dives/d1-d2-tech-tree-era-progression.md`](deep-dives/d1-d2-tech-tree-era-progression.md) | Track D phases D1/D2 - Tech-tree/prerequisite-graph generalization and era/age progression. |
| [`deep-dives/e1-e2-naval-warfare.md`](deep-dives/e1-e2-naval-warfare.md) | Track E phases E1/E2 - Water/ground terrain passability and ship combat. |
| [`deep-dives/g1-high-dpi-resolution.md`](deep-dives/g1-high-dpi-resolution.md) | Track G phase G1 - High-DPI drawable & resolution correctness (logical points versus physical pixels across window, panel, viewport and GUI). |
| [`deep-dives/g2-graphics-settings-presets.md`](deep-dives/g2-graphics-settings-presets.md) | Track G phase G2 - Graphics settings layer and `Legacy`/`Enhanced` fidelity presets. |
| [`deep-dives/g3-visual-regression-harness.md`](deep-dives/g3-visual-regression-harness.md) | Track G phase G3 - Headless golden-frame gate per preset with sharpness and edge-aliasing metrics. |
| [`deep-dives/g4-msaa.md`](deep-dives/g4-msaa.md) | Track G phase G4 - Multisample antialiasing: sample count through the main pass and a resolve before 2D. |
| [`deep-dives/g5-alpha-test-clarity.md`](deep-dives/g5-alpha-test-clarity.md) | Track G phase G5 - Cutout and alpha-test clarity: alpha-to-coverage and coverage-preserving cutout mips. |
| [`deep-dives/g6-gamma-correct-rendering.md`](deep-dives/g6-gamma-correct-rendering.md) | Track G phase G6 - Gamma-correct rendering: sRGB colour views, linear shading and mips, dithered output. |
| [`deep-dives/g7-texture-filtering.md`](deep-dives/g7-texture-filtering.md) | Track G phase G7 - Configurable anisotropy, mip LOD bias, and terrain texture-array mip quality. |
| [`deep-dives/g8-shadow-quality.md`](deep-dives/g8-shadow-quality.md) | Track G phase G8 - Shadow quality tier: four 2048 cascades and a receiver-side slope-scaled bias. |
| [`deep-dives/g9-resolution-independent-2d.md`](deep-dives/g9-resolution-independent-2d.md) | Track G phase G9 - Resolution-independent 2D and UI, and the dynamically drawn HUD. |
| [`deep-dives/g10-realistic-proportions.md`](deep-dives/g10-realistic-proportions.md) | Track G phase G10 - Realistic proportions: reference scale audit and presentation-only aircraft scale overrides. |
| [`deep-dives/g11-terrain-impact-decals.md`](deep-dives/g11-terrain-impact-decals.md) | Track G phase G11 - Terrain impact decals: scorch FX execution, capped decal store, terrain overlay pass, original producers, `Enhanced` lighting. |

---

## Tracks at a glance

Track layout: a prerequisite Playable Baseline track, a shared Foundations track, and parallel
Scale, External Assets, Gameplay Content, Naval Warfare, Object Physics/Terrain/Destruction,
Rendering Clarity, Storage Formats, Western Front, Mass Battle, and Multi-Unit Pathfinding tracks. Phase order is approximate; cross-track dependencies appear in each
track document.

- [Track P - Playable Baseline](track-p-playable-baseline.md) (prerequisite): P0 ReplaySketch
  arbitrary-command harness · P1 skirmish setup & player relationships · P2 skirmish AI opponent ·
  P3 victory/defeat conditions · P4 economy loop · P5 construction & production · P6 combat &
  command · P6b unit-contract validation · P7 end-to-end skirmish validation · P8 implementation
  delta remediation · P9 unit validation ground. Gates all other tracks.
- [Track A - Foundations](track-a-foundations.md): A1 headless/FTRT host (incl. A1-6 operational
  recording and checkpoint re-simulation) · A2 statistical
  validation · A3 simulation/presentation seam · A4 experiment framework seams & harness
  (incl. the Simulation Domain Model seam - RTS, city-builder, transport, … - and the A4-4 domain
  state projection) · A5 scenario
  authoring & visualization (incl. authoring/visualization at scale - the ReplaySketch tooling
  scales with the sim).
- [Track B - Scale](track-b-scale.md): B1 profiling & benchmarking utilities · B2 allocation cleanup · B3
  parallel/data-oriented logic · B4 spatial & pathfinding scaling · B4a larger maps (+
  large-population scenario authoring in WorldBuilder) · B5 rendering at scale · B6
  distributed simulation · B7 variable simulation fidelity.
- [Track C - External Models & Animations](track-c-external-assets.md): C1 import
  abstraction · C2 skeleton/animation retargeting · C3 material mapping · C4 first production
  format (glTF static + skinned) · C4a EE2 NIF 10.1 static-model proof · C4b EE2 cooked
  textures · C4c EE2 rigid KF animation · C4d EE2 skinned character animation · C4e EE2
  property animation · C4f EE2 scale animation · C4g EE2 morph animation · C4h EE2 particle
  effects · C4i EE2 legacy KF formats · C5
  authoring/validation tooling · C6 extended features (deferred: morph targets, advanced PBR) ·
  C7a SC2 CASC storage · C7b SC2 static M3 models · C7c SC2 skinned M3 animation · C8a-C8c
  BFME-family W3D (corpus, shader-material meshes, skinning and animation) · C9a-C9d Total War:
  THREE KINGDOMS (packs, RMV2 parts, animation, composite soldiers) · C10a-C10d AoE3 DE (archives,
  static Granny models, skinned animation, deferred Granny decompression) · C11a-C11c Rise of
  Legends (archives, BH3 models, BHA animation) · C12a-C12c Total War: SHOGUN 2 (packs, static
  models and parts, animation and composite soldiers).
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
  (`Legacy`/`Enhanced`) · G3 visual regression harness · G4 multisample antialiasing · G5
  cutout & alpha-test clarity (the screen-door fix) · G6 gamma-correct rendering · G7 texture
  filtering & mip quality · G8 shadow quality tier · G9 resolution-independent 2D & UI ·
  G10 realistic proportions (optional scale-correct aircraft presentation) · G11 terrain impact
  decals (persistent scorch marks). Not gated on Track P.
- [Track H - Map & Unit Storage Formats](track-h-storage-formats.md): H1 format requirements &
  current-state audit · H2 map interchange abstraction · H2a shared unit-content import layer · H2b metric core units · H3 new map storage format ·
  H4 new object/unit-definition storage format · H5 legacy migration & compatibility tooling · H6
  authoring & pipeline integration (WorldBuilder, ModCollector) · H7 validation & round-trip
  regression. Shared importing and persistence/schema design, distinct from B3/B4a-5's runtime-performance
  focus. Storage phases depend on Track P; H2a depends on P9 and C4d and runs before M2.
- [Track W - Western Front Simulation](track-w-western-front.md): W0 charter fixture · W1
  aggregate sector simulation · W2 fidelity promotion and reconciliation · W3a Sedan 1870 · W3b
  Somme sector · W4 command and logistics experiment · W5 distributed regional simulation · W6
  long-run campaign experiment. Depends on A4-1/A4-4, then A2, A4-3, R2, A5, and B6.
- [Track M - Mixed-Roster Mass Battle](track-m-mass-battle.md): M0 battle map · M1 capacity
  probe · M2 EE2 combat units · M3 mixed roster normalization · M4 army-scale AI · M5 1000 vs 1000
  battle (in progress: scenario, batch chain, and spectator done; full-scale acceptance batches,
  replay, and the live-fps measurement pending). Depends on P9 and A1, then B1, H2a, C4d, A4, B4,
  B5, and A5.
- [Track N - Multi-Unit Pathfinding Performance, Deadlocks, and Layered Navigation](track-n-pathfinding-performance.md):
  N0 path cost baseline · N1 search core efficiency · N2 shared group requests · N3
  shared-destination fields (gated) · N4 off-thread order paths · N5 scale verification (N0-N5
  done) · N6 deadlock detection baseline · N7 path liveness · N8 blocked-unit resolution · N9
  destination crowding · N10 deadlock verification · N11 passage detection and baseline · N12
  passage queues · N13 opposing flow and group entry · N14 passage verification · N15 layered
  navigation contract and baseline · N16 strategic sector graph · N17 corridor main path · N18
  group local router · N19 unit steering · N20 layered navigation verification · N21 structure
  passability coverage. Depends on B1,
  B4, and M4.
- [Track R - Real-World Terrain](track-r-real-world-terrain.md): R1 geospatial import core ·
  R2 standalone payload and dev preview · R3 map package output · R4 WorldBuilder import
  workflow · R5 validation and scale proof. Formerly Track B phase B4b; card IDs keep the B4b
  prefix. R1 and R2 have no dependencies; R3 depends on B4a and H3, R4 on H6.

- [Track X - Miscellaneous Fixes and Tweaks](track-x-fixes-tweaks.md) is open-ended. X1 adds imported-unit hit feedback and clearer duel captures after H2a and P9; the published M2 tank is a card-level prerequisite, not a dependency on full M2 completion. X3 evaluates open-source cursor replacements and has no dependencies. X4 moves the ReplaySketch tab strip to an Avalonia `TabControl` and has no dependencies. X5 right-sizes parallel render culling dispatch after M1. X6 extracts navigation logic from `AIUpdate` into its own class after N11.

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
- Track H's H2a supplies shared unit-content importing before M2, independently of H1/H2/H3.
  Track C supplies visuals; H4 stores H2a's representation. Its H3 (new map storage format) reserves a terrain section for
  [B4a-5's chunked heightmap representation](deep-dives/b4a-larger-maps.md) rather than competing
  with it: B4a-5 owns the runtime chunking/streaming model, H3 owns the on-disk container and
  versioning. Track R consumes both (R3) and H6's WorldBuilder integration (R4) as the output path for
  generated geospatial maps. H2's
  format-agnostic interchange abstraction mirrors [C1](track-c-external-assets.md)'s
  import-abstraction pattern applied to maps; H2a owns unit-content interchange and materialization.
  H4/H6 feed Track D's D3 external content ingestion and the standalone [ModCollector
  tool](plans/mod-collector.md) once the new object-definition format exists.
- Track W builds on Track A: W0 needs A4-1 and the A4-4 state projection, W1 needs A2, W2 needs
  A4-3. W2 is the first B7-4 group-aggregation instance and does not wait for B7. W3a consumes
  Track R's R2 payload, W4 and W6 consume A5, and W5 consumes B6.
- Track N follows B4 and M4: it reuses B4's async queue and group movement and measures on M4's
  army-AI probe. E1's surface layers make N1's region labels and N16's sector graph per-layer,
  and A4-32 wraps N's `Navigation` surface. N15 builds on X6's `UnitNavigation`. M5 does not
  depend on N.

---

## Progress

### Track X - Miscellaneous Fixes and Tweaks

| Phase | Progress | Items |
|---|---|---|
| X1 - Imported-unit hit feedback | `██████████` | 7 / 7 |
| X2 - Capacity-probe graphics resource fixes | `█████░░░░░` | 1 / 2 |
| X3 - Cursor modernization | `░░░░░░░░░░` | 0 / 1 |
| X4 - ReplaySketch TabControl shell | `░░░░░░░░░░` | 0 / 1 |
| X5 - Render culling dispatch | `░░░░░░░░░░` | 0 / 1 |
| X6 - AIUpdate navigation extraction | `██████████` | 1 / 1 |

Track X has no terminal completion target; this table covers authored phases only.

Progress counts follow the [implementation checklist](implementation-checklist.md). Updates occur when checklist items are checked off.

### Track P - Playable Baseline

| Phase | Progress | Items |
|---|---|---|
| P0 - ReplaySketch harness | `██░░░░░░░░` | 4 / 18 |
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
| Track A - Foundations | `██████▌░░░` | 14 / 22 |
| Track B - Scale | `██▌░░░░░░░` | 8 / 32 |
| Track C - External Assets | `██▌░░░░░░░` | 9 / 35 |

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
| G1 - High-DPI & resolution correctness | `██████████` | 4 / 4 |
| G2 - Graphics settings & fidelity presets | `██████████` | 1 / 1 |
| G3 - Visual regression harness | `██████████` | 1 / 1 |
| G4 - Multisample antialiasing | `██████████` | 1 / 1 |
| G5 - Cutout & alpha-test clarity | `░░░░░░░░░░` | 0 / 1 |
| G6 - Gamma-correct rendering | `░░░░░░░░░░` | 0 / 1 |
| G7 - Texture filtering & mip quality | `░░░░░░░░░░` | 0 / 1 |
| G8 - Shadow quality tier | `░░░░░░░░░░` | 0 / 1 |
| G9 - Resolution-independent 2D & UI | `░░░░░░░░░░` | 0 / 2 |
| G10 - Realistic proportions | `░░░░░░░░░░` | 0 / 3 |
| G11 - Terrain impact decals | `█████░░░░░` | 2 / 4 |
| Track G total | `███▌░░░░░░` | 7 / 20 |

### Track H - Map & Unit Storage Formats (H2a gated on P9 and C4d)

| Phase | Progress | Items |
|---|---|---|
| H1 - Format requirements & current-state audit | `░░░░░░░░░░` | 0 / 3 |
| H2 - Format-agnostic map interchange abstraction | `░░░░░░░░░░` | 0 / 2 |
| H2a - Unit-content import layer | `█████████░` | 18 / 19 |
| H2b - Metric core units | `░░░░░░░░░░` | 0 / 18 |
| H3 - New map storage format | `░░░░░░░░░░` | 0 / 5 |
| H4 - New object/unit-definition storage format | `░░░░░░░░░░` | 0 / 1 |
| H5 - Legacy migration & compatibility tooling | `░░░░░░░░░░` | 0 / 1 |
| H6 - Authoring & pipeline integration | `░░░░░░░░░░` | 0 / 1 |
| H7 - Validation & round-trip regression | `░░░░░░░░░░` | 0 / 1 |
| Track H total | `███░░░░░░░` | 18 / 51 |

### Track W - Western Front Simulation (gated on A4-1 and A4-4)

| Phase | Progress | Items |
|---|---|---|
| W0 - Charter fixture | `░░░░░░░░░░` | 0 / 9 |
| W1 - Aggregate sector simulation | `░░░░░░░░░░` | 0 / 7 |
| W2 - Fidelity promotion and reconciliation | `░░░░░░░░░░` | 0 / 14 |
| W3a - Sedan 1870 | `░░░░░░░░░░` | 0 / 31 |
| W3b - Somme sector | `░░░░░░░░░░` | 0 / 6 |
| W4 - Command and logistics experiment | `░░░░░░░░░░` | 0 / 6 |
| W5 - Distributed regional simulation | `░░░░░░░░░░` | 0 / 8 |
| W6 - Long-run campaign experiment | `░░░░░░░░░░` | 0 / 8 |
| Track W total | `░░░░░░░░░░` | 0 / 89 |

### Track M - Mixed-Roster Mass Battle (gated on P9 and A1)

| Phase | Progress | Items |
|---|---|---|
| M0 - Battle map | `██████████` | 3 / 3 |
| M1 - Capacity probe | `██████████` | 3 / 3 |
| M2 - EE2 combat units | `██████████` | 4 / 4 |
| M3 - Mixed roster normalization | `██████████` | 3 / 3 |
| M4 - Army-scale AI | `██████████` | 3 / 3 |
| M5 - 1000 vs 1000 battle | `░░░░░░░░░░` | 0 / 4 |
| Track M total | `████████░░` | 16 / 20 |

### Track N - Multi-Unit Pathfinding Performance, Deadlocks, and Layered Navigation (gated on B4 and M4)

| Phase | Progress | Items |
|---|---|---|
| N0 - Path cost baseline | `██████████` | 2 / 2 |
| N1 - Search core efficiency | `██████████` | 3 / 3 |
| N2 - Shared group requests | `██████████` | 2 / 2 |
| N3 - Shared-destination fields | `██████████` | 1 / 1 |
| N4 - Off-thread order paths | `██████████` | 2 / 2 |
| N5 - Scale verification | `██████████` | 1 / 1 |
| N6 - Deadlock detection baseline | `██████████` | 2 / 2 |
| N7 - Path liveness | `██████████` | 2 / 2 |
| N8 - Blocked-unit resolution | `██████████` | 2 / 2 |
| N9 - Destination crowding | `██████████` | 2 / 2 |
| N10 - Deadlock verification | `██████████` | 1 / 1 |
| N11 - Passage detection and baseline | `██████████` | 2 / 2 |
| N12 - Passage queues | `░░░░░░░░░░` | 0 / 2 |
| N13 - Opposing flow and group entry | `░░░░░░░░░░` | 0 / 2 |
| N14 - Passage verification | `░░░░░░░░░░` | 0 / 1 |
| N15 - Layered navigation contract and baseline | `░░░░░░░░░░` | 0 / 2 |
| N16 - Strategic sector graph | `░░░░░░░░░░` | 0 / 2 |
| N17 - Corridor main path | `░░░░░░░░░░` | 0 / 2 |
| N18 - Group local router | `░░░░░░░░░░` | 0 / 3 |
| N19 - Unit steering | `░░░░░░░░░░` | 0 / 2 |
| N20 - Layered navigation verification | `░░░░░░░░░░` | 0 / 1 |
| N21 - Structure passability coverage | `██████████` | 1 / 1 |
| Track N total | `██████░░░░` | 23 / 40 |

### Track R - Real-World Terrain

| Phase | Progress | Items |
|---|---|---|
| R1 - Geospatial import core | `███████░░░` | 4 / 6 |
| R2 - Standalone payload and dev preview | `░░░░░░░░░░` | 0 / 2 |
| R3 - Map package output | `░░░░░░░░░░` | 0 / 1 |
| R4 - WorldBuilder import workflow | `░░░░░░░░░░` | 0 / 1 |
| R5 - Validation and scale proof | `░░░░░░░░░░` | 0 / 1 |
| Track R total | `░░░░░░░░░░` | 0 / 11 |

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
