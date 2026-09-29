# Track B: Scale

> Part of the [Large-Scale RTS Roadmap](README.md). This track scales the engine to very large
> unit counts and maps. It builds on [Track A: Foundations](track-a-foundations.md).
>
> Near-term scale anchor: 10k units at interactive rate (~30 fps) on one machine. See the
> [scale-anchor Decision](open-questions.md). B2–B5 and B7 use that anchor as the first budgeted
> target; the larger {100k, millions} figures remain directional until it is raised.
>
> Architecture context: see [`agent/current-state.md`](agent/current-state.md).
>
> Audience: human overview of the scale track.

> The Milestone column states the demonstrable outcome that marks each phase done. Granular steps live in
> [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| B1. Profiling & robust benchmarking utilities | Build a reusable benchmarking harness, not ad-hoc scripts, plus repeatable large-unit scenarios such as N-unit stress scenarios. Capture baselines for logic tick, partitioning, pathfinding, object physics, and rendering, and establish the metrics the framework optimizes. Robustness requirements: repeatable/seeded scenarios; statistical rigor with multiple runs, warmup discarded, and mean plus variance/confidence interval reported; regression detection against stored baselines with tolerance thresholds; machine-readable output such as JSON/CSV for CI and the [visualizer](deep-dives/a5-visualization.md); per-subsystem timing/allocation/throughput counters; and a stable API every other track reuses to measure its own experiments. Hardware characterization is a first-class output: each run probes CPU model/core count/frequency, memory bandwidth, and GPU device/VRAM, and includes that context in the machine-readable report so results from different hardware are directly comparable and achievable simulation capacity can be inferred per machine. | A seeded N-unit scenario runs repeatedly and emits machine-readable mean+variance, including Bepu step/contact/sync counters when physics is active, with regression detection and hardware context. | Not started | — |
| B2. Allocation & hot-path cleanup | Eliminate per-tick allocations called out in code (object iteration and collision-pair lists), cache hot queries (terrain height), and tighten data layout. Do not optimize the legacy solid-pair pass that F4a retires for Bepu-managed pairs. | The named hot paths measure at/near zero allocation without a performance regression in the B1 harness. | Not started | B1 |
| B3. Parallel & data-oriented logic | Introduce intra-tick parallelism with explicit conflict resolution through intent/command buffers and authority rules; intents commit in a fixed order independent of worker count so runs reproduce within a run identity ([determinism Decision](open-questions.md)). Per the [ECS-adoption Decision](open-questions.md) this is an incremental shift of hot module data toward data-oriented / SoA layouts behind the existing module API (no full ECS rewrite). | A hot logic system ticks data-parallel via intent/command buffers while preserving its correctness invariants. | Not started | B2 |
| B4. Spatial & pathfinding scaling | Hierarchical/flow-field pathfinding and partitioning that scales to very large maps and unit counts; group movement; async path requests. | Large-unit group movement pathfinds on a large map via hierarchical/flow-field paths within benchmark budget. | Not started | B1 |
| B4a. Larger maps (via WorldBuilder) | Lift map-size limits and make authoring/editing/loading/rendering of very large maps practical, and scale WorldBuilder beyond terrain to author large object populations (sim-scale scenario setup), so the authoring tool keeps pace with the simulation. Driven first through the WorldBuilder editor. Detailed design: [deep-dives/b4a-larger-maps.md](deep-dives/b4a-larger-maps.md). | A very large map with a large object population round-trips through WorldBuilder (author → save → load) and plays. | Not started | B1, A5-6 |
| B4b. Geospatial map generation | Add a WorldBuilder import pipeline that generates an editable Panopticon map from Google Earth KML/KMZ plus georeferenced DEM and imagery rasters exported from operator-authorized Google Earth Engine datasets or other sources. Convert source coordinates into a declared local projection, resample terrain and imagery, preserve source/projection metadata, and never fetch from Google services directly. Google basemap imagery and terrain are not supported inputs. See the [deep dive](deep-dives/b4b-geospatial-map-generation.md) and [geospatial-import Decision](open-questions.md). | A selected real-world area imports with its extent, elevations, imagery, and named vector landmarks within declared tolerances, then round-trips through WorldBuilder and plays. | Not started | B4a, H6 |
| B5. Rendering at scale | GPU instancing for repeated unit models, LOD/impostors, culling, and render-bucket/shadow improvements so presentation keeps up with large logic scenes. | A large repeated-model scene renders via GPU instancing + LOD/culling within frame budget. | Not started | A3 |
| B6. Distributed simulation | Partition a single simulation across processes/machines via spatial sharding. Each RTS shard owns one Bepu world; owned bodies simulate, halo bodies are kinematic proxies, and handoff carries handle-free F4a state. Detailed design: [deep-dives/b6-distributed-simulation.md](deep-dives/b6-distributed-simulation.md). | One simulation runs across at least two shards with no object/body loss or duplication and no double-resolved boundary contact. | Not started | A1, B3, F4a |
| B7. Variable simulation fidelity | Scoped to three sub-phases: B7-1 fidelity model, `FidelityRegistry`, replicated policy inputs, and domain-state projection; B7-4 a reusable group aggregation contract generalized from W2, with error hooks; B7-6 an A5 visualization bridge and a transition and error report. Other fidelity techniques (temporal LOD, simplified behavior, precision) are deferred. F4a participation remains stable while low-fidelity physical objects sleep, become kinematic, or use aggregate colliders. Detailed design: [deep-dives/b7-variable-fidelity.md](deep-dives/b7-variable-fidelity.md). | Fidelity states are registered, projected, and visualized; W2 aggregation runs through the reusable contract; a report declares each fidelity policy's error against the full-fidelity reference. | Not started | B1, B3, F4a |

## Phase resources

Navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams marked `†` are placeholder stubs; replace them before the first card for that phase is marked `Ready`.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| B1 | [Benchmarking](deep-dives/b1-benchmarking.md) | [§](deep-dives/b1-benchmarking.md#architecture-diagrams) | `WI-B1-01..07` ([plan](implementation/track-b-b1-benchmarking.md)) |
| B2 | — | — | — |
| B3 | — | — | — |
| B4 | — | — | — |
| B4a | [Larger maps](deep-dives/b4a-larger-maps.md) | [§](deep-dives/b4a-larger-maps.md#architecture-diagrams) | `WI-B4a-01..11` ([plan](implementation/track-b-b4a-larger-maps.md)) |
| B4b | [Geospatial map generation](deep-dives/b4b-geospatial-map-generation.md) | [§10](deep-dives/b4b-geospatial-map-generation.md#10-architecture-diagrams) | `WI-B4b-01..24` ([plan](implementation/track-b-b4b-geospatial.md)) |
| B5 | — | — | — |
| B6 | [Distributed sim](deep-dives/b6-distributed-simulation.md) | [§9](deep-dives/b6-distributed-simulation.md#9-architecture-diagrams) | `WI-B6-01..10` ([plan](implementation/track-b-b6-distributed-simulation.md)) |
| B7 | [Variable fidelity](deep-dives/b7-variable-fidelity.md) | [§9](deep-dives/b7-variable-fidelity.md#9-architecture-diagrams) | `WI-B7-01..07` ([plan](implementation/track-b-b7-variable-fidelity.md)) |

## Cross-track dependencies

- B5 should account for [Track C](track-c-external-assets.md) models so instancing is not W3D-specific. Its instancing/LOD/culling is also reused by WorldBuilder's editor viewport (B4a-7) and ReplaySketch's replay viewport ([A5-5](deep-dives/a5-visualization.md)), so tool rendering scales with the sim.
- B1 benchmarks cover the engine and the scenario tooling at scale because WorldBuilder authoring/edit latency (B4a-7) and ReplaySketch authoring/replay throughput ([A5-5](deep-dives/a5-visualization.md)) are on the scaling critical path.
- B1/B2 coordinate with [F4a](track-f-terrain-destruction.md): benchmark mixed
  dynamic/kinematic/static/sensor scenes, contact dispatch, transform sync, sleep/wake, and body
  churn; do not optimize the legacy solid-pair pass that F4a retires for Bepu-managed pairs.
- B4a is the natural bridge to B6; see the [larger-maps deep dive](deep-dives/b4a-larger-maps.md). B4a-7 pairs with [A5-5](deep-dives/a5-visualization.md) so the map/scenario author and run replay tools scale together.
- B4b consumes B4a's large-map runtime representation and H3's versioned map container through
  H6's WorldBuilder read/write integration. Its
  [importer](deep-dives/b4b-geospatial-map-generation.md) targets the map interchange boundary
  instead of coupling geospatial parsing to either storage implementation. B4b-1..6b (readers,
  generation, standalone payload, dev preview) need neither B4a nor H and proceed first; only
  B4b-7..9 wait on B4a-5, H2/H3, and H6.
- B6's RTS implementation gives each shard one Bepu world. Owned bodies simulate; halo bodies are
  read-only kinematic proxies; handoff carries engine-level physics state.
- B7 composes with [A2](track-a-foundations.md), B6, and F4a. Fidelity changes the cost of an
  object's resolved physical representation, not whether its definition is physical.
- [Track W](track-w-western-front.md) consumes B4b (W3a Sedan 1870 and W3b Somme sector), B6 (W5 regional shards), and B7-4: W2's formation promotion is the first group-aggregation instance and does not wait for B7.
- Save/load is single-node only. Per the [StatePersister-reach Decision](open-questions.md), the `StatePersister` save/persist path serves single-node runs, and B3 SoA storage stays save-compatible. It does not need to serialize sharded B6 state. Distributed runs analyze and replay through the recorded snapshot/delta stream, not `StatePersister`.
