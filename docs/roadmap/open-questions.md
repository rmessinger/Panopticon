# Open Questions

> Part of the [Large-Scale Simulation Roadmap](README.md). A decision log: each entry that was
> once an open question now carries a Decision (with rationale), threaded into the affected
> phase in its track document. No questions are currently open — add new ones here as they
> arise, and convert them to a Decision (and update the affected phase) once settled.

- Determinism vs. parallelism — Decision: relaxed/statistical determinism + free intra-tick
  parallelism with explicit conflict resolution. Bit-exact, cross-platform reproducibility is
  not a goal. Results must be stable enough to compare experiments (aggregate metrics and
  invariants reproducible within tolerance), not bit-identical. Within a tick, data-parallel
  systems may run concurrently and write freely; conflicting writes are reconciled by
  well-defined conflict-resolution rules (intent/command buffers + a deterministic-enough
  merge/authority step) rather than by forbidding parallelism. Consequences threaded through the
  roadmap:
  - [A2](track-a-foundations.md) is recast from "bit-exact determinism" to reproducibility
    & statistical stability — seeded randomness stays centralized (so experiments are
    controllable and re-runnable), but the verification harness checks metric/invariant
    stability within tolerance and conflict-resolution correctness, not bit-equality.
  - [B3](track-b-scale.md) adopts the free-intra-tick + conflict-resolution model as its
    concrete parallelism design (read/modify via intent buffers, merge by rules).
  - [B6](deep-dives/distributed-simulation.md) validates handoff/boundary sync by
    invariants (no unit duplication/loss, conservation) and statistical metric closeness to a
    single-shard reference — not by bit-exact state-hash equality. Cross-shard conflicts use the
    same authority/resolution rules.
  - State hashing remains useful as a coarse/quantized signature and as an opt-in strict mode
    for debugging, but is no longer the primary divergence check; recorded state snapshots/
    deltas (not input-only re-simulation) back the replayer, since re-running inputs will not
    reproduce a run bit-for-bit. To still locate where runs diverge, A1-5 records and A5-4
    localizes via a tiered, hierarchical recording (aggregate metrics → per-region/entity
    signature tree → triggered per-entity detail) captured live — see
    [visualization.md §5](deep-dives/visualization.md).
- ECS adoption depth — Decision: incremental data-oriented storage behind the module API
  (no full ECS rewrite). Settled as a consequence of the Simulation-domain scope & mapping
  Decision below (mapping (2)): since every domain — RTS and non-RTS alike — is hosted by
  extending the `GameObject`/module system rather than separate runtimes, a single incremental
  data-oriented (SoA-style) storage behind the existing module API serves all domains. Hot
  module data migrates toward data-oriented layouts opportunistically (driven by
  [B1](track-b-scale.md) benchmarks) while the module API and composition model stay intact.
  Affects [B3](track-b-scale.md).
- Distributed model — Decision: spatial sharding. One world partitioned by region across
  nodes (chosen over lockstep peers, which caps scale at a single machine's capacity). Trades
  that ceiling for cross-shard handoff and boundary sync. See
  [B6 deep dive](deep-dives/distributed-simulation.md). Affects
  [B6](track-b-scale.md) and informs [A2](track-a-foundations.md)/[B3](track-b-scale.md).
- glTF scope for C4 — Decision: static meshes + skinned skeletal animation only. The first
  concrete external format ([C4](track-c-external-assets.md)) commits to static geometry and
  skinned skeletal animation — the minimum that gets externally-authored units (mesh + skeleton
  + gameplay-driven animation) into the engine. Morph targets (blendshapes) and advanced glTF
  PBR material extensions are deferred to a later, explicitly-deferred phase
  ([C6](track-c-external-assets.md)), added only if an experiment needs them. Keeps the first
  importer small and proves the C1 abstraction end-to-end fastest.
- Large-map ceiling & compatibility — Decision: no size cap; no compatibility constraint.
  Maps scale arbitrarily (bounded by disk, not RAM/format), to be grown gradually for scaling
  experiments; OpenSAGE may extend/replace the `.map` format and need not stay loadable in the
  original engine. Makes chunked storage (B4a-5) a core requirement. See
  [B4a deep dive](deep-dives/larger-maps.md).
- Variable-fidelity determinism stance — Decision: fidelity is a function of replicated
  simulation state for anything shared/distributed; view-driven fidelity is allowed only for
  single-node, non-distributed experiments. Downstream of the relaxed-determinism Decision
  (exact reproducibility is not required), so the real constraints are bounded statistical
  error and cross-shard consistency — fidelity inputs for shared/distributed runs must be
  derived from replicated state so all shards independently agree on each entity's fidelity level
  (never from a local camera/view, which shards don't share). View-driven fidelity (e.g. LOD by
  camera distance) remains available as an optimization for single-node experiments where there is
  no shard to disagree. Affects [B7](deep-dives/variable-fidelity.md) and
  [A2](track-a-foundations.md)/[B6](deep-dives/distributed-simulation.md).
- Asset licensing — Decision: out of scope (single-user tool). For the foreseeable future
  this is a personal experimentation tool used by one operator, so import tooling
  ([C5](track-c-external-assets.md)) does not enforce or track provenance/licensing. This is a
  deliberate, revisitable stance: if the tool is ever distributed, shared, or used to
  redistribute external assets, provenance/license capture and validation must be revisited before
  that happens (the engine still ships no assets). Until then, respecting source licenses is the
  operator's manual responsibility.
- Simulation-domain scope & mapping — Decision: validate against a transport/graph domain
  first; host non-RTS domains by extending the existing `GameObject`/module system. The
  [Simulation Domain Model seam](track-a-foundations.md) keeps the domain pluggable (RTS first,
  then transport/graph, later city builder / agent-based). Two sub-questions are now settled:
  - (1) First non-RTS validation domain = transport/graph. Chosen over a city-builder because
    a graph/network domain best stresses non-spatial partitioning (graph-cut rather than a 2-D
    grid), edge-coupled interaction locality, and edge-owner resolution authority — exactly the
    parts of the A4 seam, [B6](deep-dives/distributed-simulation.md) partitioning, and A5-4
    divergence bucketing that an RTS-only design would leave RTS-shaped. It forces the seam to be
    genuinely topology-general early.
  - (2) Mapping strategy = extend the `GameObject`/module system, not parallel domain adapters
    over the B3 data-oriented storage. The module-based composition engine is intended to be
    generic enough to host non-RTS entities/interactions (transport vehicles/links as
    `GameObject`s with domain-specific modules), so a domain is expressed as a set of modules +
    a topology/interaction adapter rather than a separate runtime. This keeps one entity runtime,
    one save/persist path, and one set of B3 storage optimizations behind the domain seam. (Also
    resolves the storage half of the ECS adoption depth question toward incremental,
    behind the module API rather than a separate ECS substrate per domain.)
  - Affects [A4](track-a-foundations.md), [A1](deep-dives/headless-host.md),
    [B6](deep-dives/distributed-simulation.md), and [B3](track-b-scale.md).
- P0 harness — runtime object references — Decision: implicit auto-handles + opt-in names + query
  selectors. Authored commands in the [P0 harness](deep-dives/playable-baseline-harness.md)
  reference an object an earlier command created (a produced unit, a built structure) by an
  implicit handle keyed to the producing command, resolved at dispatch from build/production
  results — so an author normally writes nothing ("attack the barracks built in command #3" just
  works). An explicit name is opt-in sugar for readability/disambiguation. Objects the script did
  not create (enemy/world objects such as an attack target) are referenced by a small set of
  query selectors (faction/relationship/definition/proximity) resolved against `GameLogic` at
  dispatch. Chosen over named-handles-only (forces noisy boilerplate and can't name pre-placed or
  enemy objects) and query-only (non-deterministic and fragile for the script's own objects). This
  generalizes today's `AfterActionTiming`/`DeferredAction` trigger model. Affects
  [P0](track-p-playable-baseline.md) (P0-1).
- P0 harness — assertion DSL — Decision: JSON predicates over a fixed vocabulary, with a
  code-defined escape hatch. The authored assertion surface for the
  [P0 harness](deep-dives/playable-baseline-harness.md) is declarative JSON predicates (e.g.
  `{path: "player[1].money", op: ">=", value: 0}`) over a fixed path/operator vocabulary spanning
  outcome, state, and per-tick invariant assertions — serializable into scenario files, trivially
  machine-readable, and cheap for low-effort agents/CI to author. Complex invariants that exceed the
  vocabulary use an escape hatch of code-defined (C#) predicates registered by name and
  referenced from the scenario. Chosen over JSON-only (too limiting for rich invariants), a
  small expression language (needs a parser/evaluator for marginal ergonomic gain), and
  code-only (not serializable or agent-authorable). Affects
  [P0](track-p-playable-baseline.md) (P0-4).
- P-track object ownership tracking — Decision: maintained per-`Player` owned-object set/counts.
  Per-`Player` building/unit counts (and an owned-object set) are maintained incrementally on
  create/transfer/`OnDie`/`Destroy`, giving O(1) ownership checks. Chosen over an on-demand
  all-objects scan (the `Player.GetEnergy` pattern, `Player.cs:92-104`) because the same state is
  queried frequently by the victory check ([P3](track-p-playable-baseline.md)) and the AI decision
  loop ([P2](track-p-playable-baseline.md)), and the maintained counts are friendly to the scale
  tracks. Realized as a foundational sub-phase [P1-5](deep-dives/skirmish-setup.md) (owned by P1,
  set up alongside players) consumed by both consumers rather than duplicated in either. Affects
  [P1](track-p-playable-baseline.md), [P2](track-p-playable-baseline.md),
  [P3](track-p-playable-baseline.md).
- Skirmish AI actuation — Decision: emit `Order`s through `OrderProcessor`. The AI issues the
  same `Order`s a human would (`BuildObject`/`CreateUnit`/`ForceAttack…`) rather than calling engine
  APIs directly, so AI actions enter the recorded/replayed order stream — keeping runs reproducible
  for [A2](track-a-foundations.md)/Track B and consistent with the
  [P0 harness](deep-dives/playable-baseline-harness.md). Trade-off accepted: the AI must synthesize
  valid order arguments (positions, object ids). Must be settled before P2-2. Affects
  [P2](track-p-playable-baseline.md).
- Skirmish AI competence scope — Decision: minimum legal opponent in Track P; configurable
  difficulty tiers designed but deferred. Track P implements only a minimum legal AI — it builds,
  economizes, produces, and mounts at least one attack so a match terminates win/lose; it is not
  balanced or tournament-grade. The AI is structured for configurable difficulty tiers, but only
  the minimum tier is implemented now; richer behavior (reactive defense, teching, multi-wave
  pressure) is later expansion (a candidate [A4](track-a-foundations.md) pluggable-AI experiment
  seam). Affects [P2](track-p-playable-baseline.md).
- Single-player `LocalPlayer` identification — Decision: explicit human-slot flag at setup. The
  skirmish/lobby config marks which slot is the local human; `PlayerManager` reads that flag instead
  of the ad-hoc positional `_players[2]` fallback (`PlayerManager.cs:39-46`). Affects
  [P1](track-p-playable-baseline.md).
- Skirmish spawn path — Decision: one shared SP+MP skirmish spawn. Single-player becomes "a
  skirmish with one human + AI slots," driven through the same start-position/starting-building/camera
  code as multiplayer (`Game.cs:745-763`), removing the SP/MP split. Affects
  [P1](track-p-playable-baseline.md).
- Victory defeat rule — Decision: real Generals rule by default, configurable. A player is
  defeated when they have no command/production structures and no means to rebuild (no
  dozer/worker + insufficient resources), per Generals skirmish rules — implemented as the default
  but exposed as a configurable/data-driven rule so experiments can swap simpler/stricter
  conditions. Affects [P3](track-p-playable-baseline.md).
