# Track B — Scale

> Part of the [Large-Scale RTS Roadmap](README.md). Scaling the engine toward very large unit
> counts and maps. Builds on [Track A — Foundations](track-a-foundations.md); see
> [`agent/current-state.md`](agent/current-state.md) for the architecture these phases modify.
>
> Near-term scale anchor: the first committed waypoint is 10k units at interactive rate
> (~30 fps) on one machine (see the [scale-anchor Decision](open-questions.md)). B2–B5 and B7 are
> designed and budgeted against this anchor; the larger {100k, millions} figures stay directional
> until it is raised.
>
> Audience: human — strategic track overview.

> The Milestone column states the single demonstrable outcome that marks each phase done (its
> definition-of-done); the granular steps to get there live in
> [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| B1. Profiling & robust benchmarking utilities | Build a reusable benchmarking harness (not ad-hoc scripts) and repeatable large-unit scenarios (e.g. N-unit stress scenarios), capturing baselines for logic tick, partitioning, pathfinding, and rendering. Establish the metrics this whole framework optimizes. Robustness requirements: repeatable/seeded scenarios; statistical rigor (multiple runs with warmup discarded, report mean + variance/confidence interval); regression detection against stored baselines with tolerance thresholds; machine-readable output (e.g. JSON/CSV) consumable by CI and the [visualizer](deep-dives/visualization.md); per-subsystem timing/allocation/throughput counters; and a stable API every other track reuses to measure its own experiments. | A seeded N-unit scenario runs repeatedly and emits machine-readable mean+variance with regression detection against a stored baseline. | Not started | — |
| B2. Allocation & hot-path cleanup | Eliminate per-tick allocations called out in code (object iteration list copy, collision-pair lists), cache hot queries (terrain height), and tighten data layout for cache efficiency. | The called-out per-tick allocations measure at/near zero in B1 counters with unchanged behavior. | Not started | B1 |
| B3. Parallel & data-oriented logic | Introduce free intra-tick parallelism with explicit conflict resolution (data-parallel systems write via intent/command buffers, merged by deterministic-enough rules per the [relaxed-determinism Decision](open-questions.md)). Per the [ECS-adoption Decision](open-questions.md) this is an incremental shift of hot module data toward data-oriented / SoA layouts behind the existing module API (no full ECS rewrite). | A hot logic system ticks data-parallel via intent/command buffers, staying within A2 comparison tolerance. | Not started | A2, B2 |
| B4. Spatial & pathfinding scaling | Hierarchical/flow-field pathfinding and partitioning that scales to very large maps and unit counts; group movement; async path requests. | Large-unit group movement pathfinds on a large map via hierarchical/flow-field paths within benchmark budget. | Not started | B1 |
| B4a. Larger maps (via WorldBuilder) | Lift map-size limits and make authoring/editing/loading/rendering of very large maps practical — and scale WorldBuilder beyond terrain to author large object populations (sim-scale scenario setup), so the authoring tool keeps pace with the simulation. Driven first through the WorldBuilder editor. Detailed design: [deep-dives/larger-maps.md](deep-dives/larger-maps.md). | A very large map with a large object population round-trips through WorldBuilder (author → save → load) and plays. | Not started | B1, A5-6 |
| B5. Rendering at scale | GPU instancing for repeated unit models, LOD/impostors, culling, and render-bucket/shadow improvements so presentation keeps up with large logic scenes. | A large repeated-model scene renders via GPU instancing + LOD/culling within frame budget. | Not started | A3 |
| B6. Distributed simulation | Partition a single simulation across processes/machines via spatial sharding. Boundary/handoff correctness is validated by invariants (no unit duplication/loss) and statistical closeness to a single-shard reference — not bit-exact sync (see the [relaxed-determinism Decision](open-questions.md)). Detailed design: [deep-dives/distributed-simulation.md](deep-dives/distributed-simulation.md). | One simulation runs across ≥2 shards/processes with no unit loss/duplication, staying statistically close to a single-shard reference. | Not started | A1, A2, B3 |
| B7. Variable simulation fidelity | A toolkit of fidelity-reduction techniques (temporal LOD, simplified behavior, group aggregation, precision) to scale unit counts by spending compute proportionally to each unit's importance. Experimental scaling lever. Detailed design: [deep-dives/variable-fidelity.md](deep-dives/variable-fidelity.md). | Applying fidelity reduction to low-importance units raises the sustainable unit count within bounded statistical error. | Not started | B1, B3 |

## Phase resources

A navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams marked `†` are placeholder stubs; they will be elaborated before the first card for that
phase is marked `Ready`.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| B1 | — | — | — |
| B2 | — | — | — |
| B3 | — | — | — |
| B4 | — | — | — |
| B4a | [Larger maps](deep-dives/larger-maps.md) | [§](deep-dives/larger-maps.md#architecture-diagrams) `†` | — |
| B5 | — | — | — |
| B6 | [Distributed sim](deep-dives/distributed-simulation.md) | [§7](deep-dives/distributed-simulation.md#7-architecture-diagrams) `†` | — |
| B7 | [Variable fidelity](deep-dives/variable-fidelity.md) | [§8](deep-dives/variable-fidelity.md#8-architecture-diagrams) `†` | — |

## Cross-track dependencies

- B5 (instanced rendering) should account for [Track C](track-c-external-assets.md) models
  so instancing is not W3D-specific. Its instancing/LOD/culling is also reused by the authoring &
  inspection tools — WorldBuilder's editor viewport (B4a-7) and ReplaySketch's replay viewport
  ([A5-5](deep-dives/visualization.md)) — so tool rendering scales with the sim.
- B1 (benchmarks) cover not just the engine but the scenario tooling at scale — WorldBuilder
  authoring/edit latency (B4a-7) and ReplaySketch authoring/replay throughput ([A5-5](deep-dives/visualization.md)) —
  since those tools are on the scaling critical path.
- B4a (chunked terrain storage) is the natural bridge to B6 (distributed simulation via
  spatial sharding) — see the [larger-maps deep dive](deep-dives/larger-maps.md). B4a-7
  (large-population authoring) pairs with [A5-5](deep-dives/visualization.md) so the map/scenario
  author and the run replay tools scale together.
- B7 (variable fidelity) composes with [A2](track-a-foundations.md) and B6 (per-shard
  fidelity hints). Under the [relaxed-determinism Decision](open-questions.md) the constraint is
  bounded statistical error and cross-shard consistency (fidelity driven by replicated state),
  not bit-exact reproducibility. See the [variable-fidelity deep dive](deep-dives/variable-fidelity.md).
- Save/load is single-node only. Per the [StatePersister-reach Decision](open-questions.md), the
  `StatePersister` save/persist path serves single-node runs (B3 SoA storage stays save-compatible);
  it is not required to serialize sharded B6 state. Distributed runs analyze and replay via the
  recorded snapshot/delta stream, not `StatePersister`.
