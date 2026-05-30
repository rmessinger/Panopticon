# Track B — Scale

> Part of the [Large-Scale RTS Roadmap](README.md). Scaling the engine toward very large unit
> counts and maps. Builds on [Track A — Foundations](track-a-foundations.md); see
> [`current-state.md`](current-state.md) for the architecture these phases modify.

| Phase | Goal | Status | Depends on |
|---|---|---|---|
| **B1. Profiling & scale benchmarks** | Build repeatable large-unit benchmarks (e.g. N-unit stress scenarios) and capture baselines for logic tick, partitioning, pathfinding, rendering. Establish the metrics this track optimizes. | Not started | — |
| **B2. Allocation & hot-path cleanup** | Eliminate per-tick allocations called out in code (object iteration list copy, collision-pair lists), cache hot queries (terrain height), and tighten data layout for cache efficiency. | Not started | B1 |
| **B3. Parallel & data-oriented logic** | Introduce **free intra-tick parallelism with explicit conflict resolution** (data-parallel systems write via intent/command buffers, merged by deterministic-enough rules per the [relaxed-determinism Decision](open-questions.md)). Likely an incremental shift of hot module data toward data-oriented / SoA layouts (a partial ECS-style storage) while keeping the module API. | Not started | A2, B2 |
| **B4. Spatial & pathfinding scaling** | Hierarchical/flow-field pathfinding and partitioning that scales to very large maps and unit counts; group movement; async path requests. | Not started | B1 |
| **B4a. Larger maps (via WorldBuilder)** | Lift map-size limits and make authoring/editing/loading/rendering of very large maps practical, driven first through the WorldBuilder editor. **Detailed design:** [deep-dives/larger-maps.md](deep-dives/larger-maps.md). | Not started | B1 |
| **B5. Rendering at scale** | GPU instancing for repeated unit models, LOD/impostors, culling, and render-bucket/shadow improvements so presentation keeps up with large logic scenes. | Not started | A3 |
| **B6. Distributed simulation** | Partition a single simulation across processes/machines via **spatial sharding**. Boundary/handoff correctness is validated by **invariants** (no unit duplication/loss) and statistical closeness to a single-shard reference — not bit-exact sync (see the [relaxed-determinism Decision](open-questions.md)). **Detailed design:** [deep-dives/distributed-simulation.md](deep-dives/distributed-simulation.md). | Not started | A1, A2, B3 |
| **B7. Variable simulation fidelity** | A toolkit of fidelity-reduction techniques (temporal LOD, simplified behavior, group aggregation, precision) to scale unit counts by spending compute proportionally to each unit's importance. Experimental scaling lever. **Detailed design:** [deep-dives/variable-fidelity.md](deep-dives/variable-fidelity.md). | Not started | B1, B3 |

## Cross-track dependencies

- **B5 (instanced rendering)** should account for [Track C](track-c-external-assets.md) models
  so instancing is not W3D-specific.
- **B4a** (chunked terrain storage) is the natural bridge to **B6** (distributed simulation via
  spatial sharding) — see the [larger-maps deep dive](deep-dives/larger-maps.md).
- **B7 (variable fidelity)** composes with [A2](track-a-foundations.md) and **B6** (per-shard
  fidelity hints). Under the [relaxed-determinism Decision](open-questions.md) the constraint is
  **bounded statistical error** and cross-shard consistency (fidelity driven by replicated state),
  not bit-exact reproducibility. See the [variable-fidelity deep dive](deep-dives/variable-fidelity.md).
