# Open Questions

> Part of the [Large-Scale RTS Roadmap](README.md). Unresolved decisions. As each is settled,
> move the answer inline here (mark it as a **Decision**) and update the affected phase in its
> track document.

- **Determinism vs. parallelism — Decision: relaxed/statistical determinism + free intra-tick
  parallelism with explicit conflict resolution.** Bit-exact, cross-platform reproducibility is
  **not** a goal. Results must be *stable enough to compare experiments* (aggregate metrics and
  invariants reproducible within tolerance), not bit-identical. Within a tick, data-parallel
  systems may run concurrently and write freely; conflicting writes are reconciled by
  **well-defined conflict-resolution rules** (intent/command buffers + a deterministic-enough
  merge/authority step) rather than by forbidding parallelism. Consequences threaded through the
  roadmap:
  - **[A2](track-a-foundations.md)** is recast from "bit-exact determinism" to **reproducibility
    & statistical stability** — seeded randomness stays centralized (so experiments are
    *controllable* and *re-runnable*), but the verification harness checks metric/invariant
    stability within tolerance and conflict-resolution correctness, not bit-equality.
  - **[B3](track-b-scale.md)** adopts the free-intra-tick + conflict-resolution model as its
    concrete parallelism design (read/modify via intent buffers, merge by rules).
  - **[B6](deep-dives/distributed-simulation.md)** validates handoff/boundary sync by
    **invariants** (no unit duplication/loss, conservation) and statistical metric closeness to a
    single-shard reference — not by bit-exact state-hash equality. Cross-shard conflicts use the
    same authority/resolution rules.
  - State hashing remains useful as a *coarse/quantized* signature and as an opt-in strict mode
    for debugging, but is no longer the primary divergence check; **recorded state snapshots/
    deltas** (not input-only re-simulation) back the replayer, since re-running inputs will not
    reproduce a run bit-for-bit. To still locate *where* runs diverge, A1-5 records and A5-4
    localizes via a **tiered, hierarchical recording** (aggregate metrics → per-region/entity
    signature tree → triggered per-entity detail) captured live — see
    [visualization.md §5](deep-dives/visualization.md).
- **ECS adoption depth:** full ECS rewrite vs. incremental data-oriented storage behind the
  existing module API. Current lean: incremental. Affects [B3](track-b-scale.md).
- **Distributed model — Decision: spatial sharding.** One world partitioned by region across
  nodes (chosen over lockstep peers, which caps scale at a single machine's capacity). Trades
  that ceiling for cross-shard handoff and boundary sync. See
  [B6 deep dive](deep-dives/distributed-simulation.md). Affects
  [B6](track-b-scale.md) and informs [A2](track-a-foundations.md)/[B3](track-b-scale.md).
- **glTF scope for C4:** static + skinned animation only, or also morph targets / PBR
  extensions? Affects [C4](track-c-external-assets.md).
- **Large-map ceiling & compatibility — Decision: no size cap; no compatibility constraint.**
  Maps scale arbitrarily (bounded by disk, not RAM/format), to be grown gradually for scaling
  experiments; OpenSAGE may extend/replace the `.map` format and need not stay loadable in the
  original engine. Makes chunked storage (B4a-5) a core requirement. See
  [B4a deep dive](deep-dives/larger-maps.md).
- **Variable-fidelity determinism stance:** for shared/distributed runs, must fidelity be
  driven *only* by replicated simulation state (never local camera/view), or do we allow
  view-driven fidelity with bounded, reconciled error? Now downstream of the relaxed-determinism
  Decision above: since exact reproducibility is *not* required, view-driven fidelity (option
  (b)) is more palatable — the real constraint becomes **bounded statistical error** and
  **consistency across shards** (fidelity must be a function of replicated state so shards agree),
  not bit-exactness. Current lean: deterministic/replicated-state inputs for anything shared or
  distributed; view-driven fidelity allowed for single-node experiments. Affects
  [B7](deep-dives/variable-fidelity.md) and
  [A2](track-a-foundations.md)/[B6](deep-dives/distributed-simulation.md).
- **Asset licensing:** external models must respect their source licenses; the engine ships
  no assets — confirm import tooling makes provenance/licensing explicit. Affects
  [C5](track-c-external-assets.md).
- **Simulation-domain scope & mapping:** the [Simulation Domain Model seam](track-a-foundations.md)
  makes the domain pluggable (RTS first, then city builder / transportation / agent-based). Open:
  (1) **which non-RTS domain to validate against first** (a transport/graph domain best stresses
  non-spatial partitioning; a city-builder best stresses flow coupling), and (2) **how non-RTS
  entity/interaction models map onto the engine** — extend the existing `GameObject`/module system
  to host them, or run domain adapters over the B3 data-oriented storage in parallel to it.
  Affects [A4](track-a-foundations.md), [A1](deep-dives/headless-host.md),
  [B6](deep-dives/distributed-simulation.md).
