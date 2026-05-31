# Open Questions

> Part of the [Large-Scale Simulation Roadmap](README.md). A **decision log**: each entry that was
> once an open question now carries a **Decision** (with rationale), threaded into the affected
> phase in its track document. **No questions are currently open** — add new ones here as they
> arise, and convert them to a Decision (and update the affected phase) once settled.

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
- **ECS adoption depth — Decision: incremental data-oriented storage behind the module API**
  (no full ECS rewrite). Settled as a consequence of the **Simulation-domain scope & mapping
  Decision** below (mapping (2)): since every domain — RTS and non-RTS alike — is hosted by
  *extending* the `GameObject`/module system rather than separate runtimes, a single incremental
  data-oriented (SoA-style) storage **behind the existing module API** serves all domains. Hot
  module data migrates toward data-oriented layouts opportunistically (driven by
  [B1](track-b-scale.md) benchmarks) while the module API and composition model stay intact.
  Affects [B3](track-b-scale.md).
- **Distributed model — Decision: spatial sharding.** One world partitioned by region across
  nodes (chosen over lockstep peers, which caps scale at a single machine's capacity). Trades
  that ceiling for cross-shard handoff and boundary sync. See
  [B6 deep dive](deep-dives/distributed-simulation.md). Affects
  [B6](track-b-scale.md) and informs [A2](track-a-foundations.md)/[B3](track-b-scale.md).
- **glTF scope for C4 — Decision: static meshes + skinned skeletal animation only.** The first
  concrete external format ([C4](track-c-external-assets.md)) commits to static geometry and
  skinned skeletal animation — the minimum that gets externally-authored *units* (mesh + skeleton
  + gameplay-driven animation) into the engine. **Morph targets (blendshapes) and advanced glTF
  PBR material extensions are deferred** to a later, explicitly-deferred phase
  ([C6](track-c-external-assets.md)), added only if an experiment needs them. Keeps the first
  importer small and proves the C1 abstraction end-to-end fastest.
- **Large-map ceiling & compatibility — Decision: no size cap; no compatibility constraint.**
  Maps scale arbitrarily (bounded by disk, not RAM/format), to be grown gradually for scaling
  experiments; OpenSAGE may extend/replace the `.map` format and need not stay loadable in the
  original engine. Makes chunked storage (B4a-5) a core requirement. See
  [B4a deep dive](deep-dives/larger-maps.md).
- **Variable-fidelity determinism stance — Decision: fidelity is a function of replicated
  simulation state for anything shared/distributed; view-driven fidelity is allowed only for
  single-node, non-distributed experiments.** Downstream of the relaxed-determinism Decision
  (exact reproducibility is not required), so the real constraints are **bounded statistical
  error** and **cross-shard consistency** — fidelity inputs for shared/distributed runs must be
  derived from replicated state so all shards independently agree on each entity's fidelity level
  (never from a local camera/view, which shards don't share). View-driven fidelity (e.g. LOD by
  camera distance) remains available as an optimization for single-node experiments where there is
  no shard to disagree. Affects [B7](deep-dives/variable-fidelity.md) and
  [A2](track-a-foundations.md)/[B6](deep-dives/distributed-simulation.md).
- **Asset licensing — Decision: out of scope (single-user tool).** For the foreseeable future
  this is a personal experimentation tool used by one operator, so import tooling
  ([C5](track-c-external-assets.md)) does **not** enforce or track provenance/licensing. This is a
  **deliberate, revisitable** stance: if the tool is ever distributed, shared, or used to
  redistribute external assets, provenance/license capture and validation must be revisited before
  that happens (the engine still ships no assets). Until then, respecting source licenses is the
  operator's manual responsibility.
- **Simulation-domain scope & mapping — Decision: validate against a transport/graph domain
  first; host non-RTS domains by extending the existing `GameObject`/module system.** The
  [Simulation Domain Model seam](track-a-foundations.md) keeps the domain pluggable (RTS first,
  then transport/graph, later city builder / agent-based). Two sub-questions are now settled:
  - **(1) First non-RTS validation domain = transport/graph.** Chosen over a city-builder because
    a graph/network domain best stresses **non-spatial partitioning** (graph-cut rather than a 2-D
    grid), edge-coupled interaction locality, and edge-owner resolution authority — exactly the
    parts of the A4 seam, [B6](deep-dives/distributed-simulation.md) partitioning, and A5-4
    divergence bucketing that an RTS-only design would leave RTS-shaped. It forces the seam to be
    genuinely topology-general early.
  - **(2) Mapping strategy = extend the `GameObject`/module system**, not parallel domain adapters
    over the B3 data-oriented storage. The module-based composition engine is intended to be
    **generic enough** to host non-RTS entities/interactions (transport vehicles/links as
    `GameObject`s with domain-specific modules), so a domain is expressed as a set of modules +
    a topology/interaction adapter rather than a separate runtime. This keeps one entity runtime,
    one save/persist path, and one set of B3 storage optimizations behind the domain seam. (Also
    resolves the storage half of the **ECS adoption depth** question toward *incremental,
    behind the module API* rather than a separate ECS substrate per domain.)
  - Affects [A4](track-a-foundations.md), [A1](deep-dives/headless-host.md),
    [B6](deep-dives/distributed-simulation.md), and [B3](track-b-scale.md).
