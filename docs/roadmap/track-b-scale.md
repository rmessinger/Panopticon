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
| B1. Profiling & robust benchmarking utilities | Build a reusable benchmarking harness, not ad-hoc scripts, plus repeatable large-unit scenarios such as N-unit stress scenarios. Capture baselines for logic tick, partitioning, pathfinding, and rendering, and establish the metrics the framework optimizes. Robustness requirements: repeatable/seeded scenarios; statistical rigor with multiple runs, warmup discarded, and mean plus variance/confidence interval reported; regression detection against stored baselines with tolerance thresholds; machine-readable output such as JSON/CSV for CI and the [visualizer](deep-dives/a5-visualization.md); per-subsystem timing/allocation/throughput counters; and a stable API every other track reuses to measure its own experiments. Hardware characterization is a first-class output: each run probes CPU model/core count/frequency, memory bandwidth, and GPU device/VRAM, and includes that context in the machine-readable report so results from different hardware are directly comparable and achievable simulation capacity can be inferred per machine. | A seeded N-unit scenario runs repeatedly and emits machine-readable mean+variance with regression detection against a stored baseline, alongside a hardware characterization summary so results from different machines are directly comparable. | Not started | — |
| B2. Allocation & hot-path cleanup | Eliminate per-tick allocations called out in code (object iteration list copy, collision-pair lists), cache hot queries (terrain height), and tighten data layout for cache efficiency. | The called-out per-tick allocations measure at/near zero in B1 counters with unchanged behavior. | Not started | B1 |
| B3. Parallel & data-oriented logic | Introduce free intra-tick parallelism with explicit conflict resolution through intent/command buffers and authority rules. Per the [ECS-adoption Decision](open-questions.md) this is an incremental shift of hot module data toward data-oriented / SoA layouts behind the existing module API (no full ECS rewrite). | A hot logic system ticks data-parallel via intent/command buffers while preserving its correctness invariants. | Not started | B2 |
| B4. Spatial & pathfinding scaling | Hierarchical/flow-field pathfinding and partitioning that scales to very large maps and unit counts; group movement; async path requests. | Large-unit group movement pathfinds on a large map via hierarchical/flow-field paths within benchmark budget. | Not started | B1 |
| B4a. Larger maps (via WorldBuilder) | Lift map-size limits and make authoring/editing/loading/rendering of very large maps practical, and scale WorldBuilder beyond terrain to author large object populations (sim-scale scenario setup), so the authoring tool keeps pace with the simulation. Driven first through the WorldBuilder editor. Detailed design: [deep-dives/b4a-larger-maps.md](deep-dives/b4a-larger-maps.md). | A very large map with a large object population round-trips through WorldBuilder (author → save → load) and plays. | Not started | B1, A5-6 |
| B5. Rendering at scale | GPU instancing for repeated unit models, LOD/impostors, culling, and render-bucket/shadow improvements so presentation keeps up with large logic scenes. | A large repeated-model scene renders via GPU instancing + LOD/culling within frame budget. | Not started | A3 |
| B6. Distributed simulation | Partition a single simulation across processes/machines via spatial sharding. Boundary and handoff correctness is validated by invariants such as no unit duplication or loss. Detailed design: [deep-dives/b6-distributed-simulation.md](deep-dives/b6-distributed-simulation.md). | One simulation runs across ≥2 shards/processes with no unit loss or duplication. | Not started | A1, B3 |
| B7. Variable simulation fidelity | A toolkit of fidelity-reduction techniques (temporal LOD, simplified behavior, group aggregation, precision) to scale unit counts by spending compute proportionally to each unit's importance. Experimental scaling lever. Detailed design: [deep-dives/b7-variable-fidelity.md](deep-dives/b7-variable-fidelity.md). | Applying fidelity reduction to low-importance units raises the sustainable unit count within bounded statistical error. | Not started | B1, B3 |

## Phase resources

Navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams marked `†` are placeholder stubs; replace them before the first card for that phase is marked `Ready`.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| B1 | — | — | — |
| B2 | — | — | — |
| B3 | — | — | — |
| B4 | — | — | — |
| B4a | [Larger maps](deep-dives/b4a-larger-maps.md) | [§](deep-dives/b4a-larger-maps.md#architecture-diagrams) | — |
| B5 | — | — | — |
| B6 | [Distributed sim](deep-dives/b6-distributed-simulation.md) | [§6](deep-dives/b6-distributed-simulation.md#6-architecture-diagrams) | — |
| B7 | [Variable fidelity](deep-dives/b7-variable-fidelity.md) | [§8](deep-dives/b7-variable-fidelity.md#8-architecture-diagrams) | — |

## Cross-track dependencies

- B5 should account for [Track C](track-c-external-assets.md) models so instancing is not W3D-specific. Its instancing/LOD/culling is also reused by WorldBuilder's editor viewport (B4a-7) and ReplaySketch's replay viewport ([A5-5](deep-dives/a5-visualization.md)), so tool rendering scales with the sim.
- B1 benchmarks cover the engine and the scenario tooling at scale because WorldBuilder authoring/edit latency (B4a-7) and ReplaySketch authoring/replay throughput ([A5-5](deep-dives/a5-visualization.md)) are on the scaling critical path.
- B4a is the natural bridge to B6; see the [larger-maps deep dive](deep-dives/b4a-larger-maps.md). B4a-7 pairs with [A5-5](deep-dives/a5-visualization.md) so the map/scenario author and run replay tools scale together.
- B7 composes with [A2](track-a-foundations.md) and B6. Its accuracy is measured statistically, while shared runs drive fidelity from replicated state so shards agree on each entity's level. See the [variable-fidelity deep dive](deep-dives/b7-variable-fidelity.md).
- Save/load is single-node only. Per the [StatePersister-reach Decision](open-questions.md), the `StatePersister` save/persist path serves single-node runs, and B3 SoA storage stays save-compatible. It does not need to serialize sharded B6 state. Distributed runs analyze and replay through the recorded snapshot/delta stream, not `StatePersister`.
