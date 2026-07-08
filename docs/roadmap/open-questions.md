# Open Questions

> Part of the [Large-Scale Simulation Roadmap](README.md). This document is the decision log.
> Each entry is a settled Decision with rationale and links back to the affected phase. No
> questions remain open; new items join here and become Decisions once settled.
>
> Audience: human reference for roadmap decisions.

- Determinism vs. parallelism. Decision: relaxed/statistical determinism + free intra-tick
  parallelism with explicit conflict resolution. Bit-exact, cross-platform reproducibility is
  not a goal. Results must be stable enough to compare experiments through aggregate metrics and
  invariants reproduced within tolerance, not bit-identical. Within a tick, data-parallel
  systems may run concurrently and write freely; conflicting writes are reconciled through
  intent/command buffers plus a deterministic-enough merge and authority step. Consequences:
  - [A2](track-a-foundations.md) is recast from "bit-exact determinism" to reproducibility
    & statistical stability: seeded randomness stays centralized (so experiments are
    controllable and re-runnable), but the verification harness checks metric/invariant
    stability within tolerance and conflict-resolution correctness, not bit-equality.
  - [B3](track-b-scale.md) adopts the free-intra-tick + conflict-resolution model as its
    concrete parallelism design (read/modify via intent buffers, merge by rules).
  - [B6](deep-dives/distributed-simulation.md) validates handoff/boundary sync by
    invariants (no unit duplication/loss, conservation) and statistical metric closeness to a
    single-shard reference, not by bit-exact state-hash equality. Cross-shard conflicts use the
    same authority/resolution rules.
  - State hashing remains useful as a coarse/quantized signature and as an opt-in strict mode
    for debugging, but is no longer the primary divergence check; recorded state snapshots/
    deltas (not input-only re-simulation) back the replayer, since re-running inputs will not
    reproduce a run bit-for-bit. To still locate where runs diverge, A1-5 records and A5-4
    localizes via a tiered, hierarchical recording (aggregate metrics → per-region/entity
    signature tree → triggered per-entity detail) captured live; see
    [visualization.md §5](deep-dives/visualization.md).
- ECS adoption depth. Decision: incremental data-oriented storage behind the module API
  (no full ECS rewrite). This follows from the Simulation-domain scope & mapping Decision below:
  every domain, RTS and non-RTS alike, is hosted by extending the `GameObject`/module system
  rather than by adding separate runtimes, so one incremental SoA-style storage layer behind the
  existing module API serves all domains. Hot module data migrates toward data-oriented layouts
  opportunistically, driven by [B1](track-b-scale.md) benchmarks, while the module API and
  composition model stay intact. Affects [B3](track-b-scale.md).
- Distributed model. Decision: spatial sharding. One world partitioned by region across
  nodes (chosen over lockstep peers, which caps scale at a single machine's capacity). Trades
  that ceiling for cross-shard handoff and boundary sync. See
  [B6 deep dive](deep-dives/distributed-simulation.md). Affects
  [B6](track-b-scale.md) and informs [A2](track-a-foundations.md)/[B3](track-b-scale.md).
- glTF scope for C4. Decision: static meshes + skinned skeletal animation only. The first
  concrete external format ([C4](track-c-external-assets.md)) covers the minimum needed to get
  externally-authored units, with mesh, skeleton, and gameplay-driven animation, into the engine.
  Morph targets (blendshapes) and advanced glTF PBR material extensions are deferred to
  [C6](track-c-external-assets.md) and added only if an experiment needs them. This keeps the
  first importer small and proves the C1 abstraction end-to-end as early as possible.
- Large-map ceiling & compatibility. Decision: no size cap; no compatibility constraint.
  Maps scale arbitrarily (bounded by disk, not RAM/format), to be grown gradually for scaling
  experiments; OpenSAGE may extend/replace the `.map` format and need not stay loadable in the
  original engine. Makes chunked storage (B4a-5) a core requirement. See
  [B4a deep dive](deep-dives/larger-maps.md).
- Variable-fidelity determinism stance. Decision: fidelity is a function of replicated
  simulation state for anything shared or distributed; view-driven fidelity is allowed only for
  single-node, non-distributed experiments. Under the relaxed-determinism Decision, the real
  constraints are bounded statistical error and cross-shard consistency. Shared and distributed
  runs therefore derive fidelity inputs from replicated state so every shard agrees on each
  entity's fidelity level, never from a local camera or view. View-driven fidelity, such as LOD
  by camera distance, remains available for single-node experiments where no shard can disagree.
  Affects [B7](deep-dives/variable-fidelity.md) and
  [A2](track-a-foundations.md)/[B6](deep-dives/distributed-simulation.md).
- Asset licensing. Decision: out of scope (single-user tool). For the foreseeable future
  this is a personal experimentation tool used by one operator, so import tooling
  ([C5](track-c-external-assets.md)) does not enforce or track provenance or licensing. Revisit
  this before the tool is distributed, shared, or used to redistribute external assets; the engine
  still ships no assets. Until then, respecting source licenses is the operator's manual
  responsibility.
- Simulation-domain scope & mapping. Decision: validate against a transport/graph domain
  first; host non-RTS domains by extending the existing `GameObject`/module system. The
  [Simulation Domain Model seam](track-a-foundations.md) keeps the domain pluggable (RTS first,
  then transport/graph, later city builder / agent-based). Two sub-questions are now settled:
  - (1) First non-RTS validation domain = transport/graph. Chosen over a city-builder because
    a graph/network domain best stresses non-spatial partitioning (graph-cut rather than a 2-D
    grid), edge-coupled interaction locality, and edge-owner resolution authority: exactly the
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
- P0 harness: runtime object references. Decision: implicit auto-handles + opt-in names + query
  selectors. Authored commands in the [P0 harness](deep-dives/playable-baseline-harness.md)
  reference an object an earlier command created (a produced unit, a built structure) by an
  implicit handle keyed to the producing command, resolved at dispatch from build/production
  results, so an author normally writes nothing ("attack the barracks built in command #3" just
  works). An explicit name is opt-in sugar for readability/disambiguation. Objects the script did
  not create (enemy/world objects such as an attack target) are referenced by a small set of
  query selectors (faction/relationship/definition/proximity) resolved against `GameLogic` at
  dispatch. Chosen over named-handles-only (forces noisy boilerplate and can't name pre-placed or
  enemy objects) and query-only (non-deterministic and fragile for the script's own objects). This
  generalizes today's `AfterActionTiming`/`DeferredAction` trigger model. Affects
  [P0](track-p-playable-baseline.md) (P0-1).
- P0 harness: assertion DSL. Decision: JSON predicates over a fixed vocabulary, with a
  code-defined escape hatch. The authored assertion surface for the
  [P0 harness](deep-dives/playable-baseline-harness.md) is declarative JSON predicates (e.g.
  `{path: "player[1].money", op: ">=", value: 0}`) over a fixed path/operator vocabulary spanning
  outcome, state, and per-tick invariant assertions: serializable into scenario files, trivially
  machine-readable, and cheap for low-effort agents/CI to author. Complex invariants that exceed the
  vocabulary use an escape hatch of code-defined (C#) predicates registered by name and
  referenced from the scenario. Chosen over JSON-only (too limiting for rich invariants), a
  small expression language (needs a parser/evaluator for marginal ergonomic gain), and
  code-only (not serializable or agent-authorable). Affects
  [P0](track-p-playable-baseline.md) (P0-4).
- P0 harness: MAUI screenshot mode. Decision: agents verify MAUI-layer changes (layout, tab
  switching, view wiring, error display) by running the app in screenshot-and-exit mode via
  `REPLAYSKETCH_SCREENSHOT_PATH`, `REPLAYSKETCH_SCREENSHOT_TAB`, and `REPLAYSKETCH_SCREENSHOT_DELAY`
  environment variables. `MainPage.OnLoaded` detects the variables, selects the requested tab, waits
  the settle delay, captures the window via `Microsoft.Maui.Media.Screenshot.Default.CaptureAsync()`,
  saves the PNG, and calls `Application.Current.Quit()`. Chosen over an external `screencapture`
  script (requires window ID detection, timing heuristics, and no communication with the app) and
  over UI-test frameworks (heavy, platform-bound, flaky). The in-app approach is self-contained,
  requires no extra tooling or permissions, and works with `dotnet run`. Implemented as P0-1a;
  documented in `src/OpenSage.Tools.ReplaySketch.Maui/AGENTS.md`. Affects
  [P0](track-p-playable-baseline.md) (P0-1a).
- P0 harness: MAUI validation reach + agent playbook. Decision: keep logic in `.Core`, validate
  headlessly through the test suite plus run-and-assert, minimize MAUI-only code behind injectable
  seams, and document the loop in a checked-in `AGENTS.md`. ReplaySketch logic stays in
  `OpenSage.Tools.ReplaySketch.Core` and is validated headlessly by
  `OpenSage.Tools.ReplaySketch.Tests` plus the [P0 harness](deep-dives/playable-baseline-harness.md)
  run-and-assert entry point (P0-5). `MainViewModel` is reduced to a thin shell by abstracting
  `Preferences.Default`, `MainThread`, and `Process.Start` or launcher behavior behind injectable
  seams (P0-1b/P0-1c), so the command surface is unit-testable without a MAUI runtime. The
  documented validation loop lives in `src/OpenSage.Tools.ReplaySketch.Maui/AGENTS.md` (P0-7),
  mirroring `OpenSage.Tools.TestHarness/AGENTS.md`. Chosen over MAUI UI automation and over leaving
  validation undocumented. Affects [P0](track-p-playable-baseline.md) (P0-1b/P0-1c/P0-7).
- P0 harness: ScenarioMatrix sweep placement. Decision: the cartesian-product matrix expansion
  primitive belongs in P0 (P0-5b), not deferred to A5. A5-2 adds tooling and visualization on
  top; it does not re-implement expansion or execution. All axis dimensions (OrderType, faction,
  map, assertion parameters) are in scope; the matrix is built out incrementally alongside
  implementation. Affects [P0](track-p-playable-baseline.md) (P0-5b) and
  [A5](track-a-foundations.md) (A5-2).
- P-track object ownership tracking. Decision: maintained per-`Player` owned-object set and
  counts. Building and unit counts, plus an owned-object set, are maintained incrementally on
  create, transfer, `OnDie`, and `Destroy`, giving O(1) ownership checks. This replaces an
  on-demand all-objects scan such as the `Player.GetEnergy` pattern (`Player.cs:92-104`) because
  the same state is queried frequently by the victory check ([P3](track-p-playable-baseline.md))
  and the AI decision loop ([P2](track-p-playable-baseline.md)), and because maintained counts fit
  the scale tracks better. Realized as foundational sub-phase
  [P1-5](deep-dives/skirmish-setup.md), owned by P1 and shared by both consumers. Affects
  [P1](track-p-playable-baseline.md), [P2](track-p-playable-baseline.md),
  [P3](track-p-playable-baseline.md).
- Skirmish AI actuation. Decision: emit `Order`s through `OrderProcessor`. The AI issues the
  same `Order`s a human would (`BuildObject`/`CreateUnit`/`ForceAttack…`) rather than calling engine
  APIs directly, so AI actions enter the recorded/replayed order stream, keeping runs reproducible
  for [A2](track-a-foundations.md)/Track B and consistent with the
  [P0 harness](deep-dives/playable-baseline-harness.md). Trade-off accepted: the AI must synthesize
  valid order arguments (positions, object ids). Must be settled before P2-2. Affects
  [P2](track-p-playable-baseline.md).
- Skirmish AI competence scope. Decision: minimum legal opponent in Track P; configurable
  difficulty tiers designed but deferred. Track P implements only a minimum legal AI: it builds,
  economizes, produces, and mounts at least one attack so a match terminates win/lose; it is not
  balanced or tournament-grade. The AI is structured for configurable difficulty tiers, but only
  the minimum tier is implemented now; richer behavior (reactive defense, teching, multi-wave
  pressure) is later expansion (a candidate [A4](track-a-foundations.md) pluggable-AI experiment
  seam). Affects [P2](track-p-playable-baseline.md).
- Pluggable AI decision policy. Decision: formalize how a player's strategic decisions get made
  (build/produce/attack) as its own A4 experiment seam now that P2 has a reference
  implementation to carve it from. `AIPlayer.OnDecisionTick()`'s current subclass-override shape
  becomes an injectable `IAIDecisionPolicy`, with `SkirmishAIPlayer`'s rule-based state machine
  as the reference implementation; actuation is unaffected: every policy still emits `Order`s
  through `OrderProcessor` (the P2 actuation decision above), so reproducibility (A2) and the
  recorded/replayed order stream hold regardless of which policy runs. This is what unblocks a
  neural-network policy trained by running many seeded self-play matches (a population of
  weight-vector genomes, fitness scored off `GameOutcome`, evolved via selection/mutation across
  generations in an A4 experiment sweep). Design-only for now; implementation (interface
  extraction, a first NN policy, and the training harness) is follow-on work. Detailed design:
  [deep-dives/pluggable-ai.md](deep-dives/pluggable-ai.md). Affects
  [A4](track-a-foundations.md), [P2](track-p-playable-baseline.md).
- Single-player `LocalPlayer` identification. Decision: explicit human-slot flag at setup. The
  skirmish/lobby config marks which slot is the local human; `PlayerManager` reads that flag instead
  of the ad-hoc positional `_players[2]` fallback (`PlayerManager.cs:39-46`). Affects
  [P1](track-p-playable-baseline.md).
- Skirmish spawn path. Decision: one shared SP+MP skirmish spawn. Single-player becomes "a
  skirmish with one human + AI slots," driven through the same start-position/starting-building/camera
  code as multiplayer (`Game.cs:745-763`), removing the SP/MP split. Affects
  [P1](track-p-playable-baseline.md).
- Victory defeat rule. Decision: real Generals rule by default, configurable. A player is
  defeated when they have no command/production structures and no means to rebuild (no
  dozer/worker + insufficient resources), per Generals skirmish rules, implemented as the default
  but exposed as a configurable/data-driven rule so experiments can swap simpler/stricter
  conditions. Affects [P3](track-p-playable-baseline.md).
- Match end-state enumeration. Decision: six end states, full `GameOutcome` type, all validated
  by P7. Three `OutcomeReason` values: `Eliminated` (standard defeat or sudden-death resolution),
  `Surrendered` (any player/AI issues `OrderType.EndGame`), `ScoreDecision` (time-limit expiry
  with configurable score-metric tiebreak). Two additional `OutcomeStatus` values beyond
  `InProgress`/`Decided`: `SuddenDeath` (transitional, mutual simultaneous elimination detected,
  match continues). Mutual elimination resolves via sudden-death continuation to the next
  `Eliminated` outcome. Time-limit expiry resolves via highest-score win (score metric is
  configurable, default = buildings standing). Team wins are expressed by setting `WinningTeam`
  rather than a separate reason. All six end states have P7 regression scenarios. Affects
  [P3](track-p-playable-baseline.md) (P3-5/P3-6), [P7](track-p-playable-baseline.md).
- Unit-contract validation scope. Decision: per-archetype full contract via P0 harness; one
  representative per role; surfaced gaps are explicit, not silent. Six archetypes cover all
  meaningful unit-behaviour variation in Generals/ZH: infantry, vehicle, aircraft, builder,
  support, superweapon. Each is validated against a full contract (produce/move/attack/every
  weapon mode/every special ability/die with ownership cleanup) via one P0 harness scenario.
  "Full" means every `SpecialPowerType` in the unit's command set and every `WeaponSlot` are
  exercised; unimplemented handlers produce explicit assertion failures (not silent no-ops) and
  become tracked P6b gap items. Archetype granularity chosen over per-faction-unit (50+ units,
  excessive) and per-role-category-only (misses ability/weapon-mode variation). Representative
  units are from the USA reference faction; the scenario pattern is faction-agnostic via the P0
  harness (any faction's unit can be substituted by name). Affects
  [P6b](track-p-playable-baseline.md) (all sub-phases), [P7](track-p-playable-baseline.md).
- Framework audience & distribution scope. Decision: single-operator personal research tool,
  whole framework. The entire simulation framework, not just the C-track import tooling, is a
  personal experimentation tool for one operator for the foreseeable future. This generalizes the
  asset-licensing Decision above: no multi-user robustness, external/third-party reproducibility, or
  distribution hardening is a goal. Human-facing tooling ([A5](track-a-foundations.md) authoring/
  visualization, [B1](track-b-scale.md) benchmarks) targets that one operator, and runs need only be
  reproducible-for-comparison by them, not citable or shareable by others. Deliberate and revisitable:
  if the framework is ever shared, published, or used to distribute assets, multi-user concerns (and
  the C5 provenance/licensing stance) must be revisited first. Affects the
  [README guiding principles](README.md), [A5](track-a-foundations.md), [B1](track-b-scale.md).
- Near-term scale anchor. Decision: 10k units at interactive rate (~30 fps) on one machine.
  The first committed waypoint toward the long-term thousands–millions goal. Track B milestones
  (B2–B5, B7) are designed and budgeted against this single anchor; the larger {100k, millions}
  figures stay directional, not yet budgeted. [B1](track-b-scale.md) fixes a seeded 10k-unit
  interactive scenario as the reference workload, and B2 (allocation), B3 (parallelism),
  B4 (pathfinding), B5 (rendering), and B7 (fidelity) each target sustaining ~10k units at ~30 fps
  single-node before the anchor is raised. Affects [B1–B5, B7](track-b-scale.md).
- Non-RTS validation domain completeness. Decision: minimal partition-stress harness. The first
  non-RTS (transport/graph) domain (see the Simulation-domain scope Decision) is implemented only as
  far as needed to exercise graph-cut (non-spatial) partitioning and edge-coupled interaction/owner
  authority, not a complete, terminating, Track-P-grade scenario. It instantiates graph entities,
  runs them headlessly, and emits the partition/boundary metrics A4/B6 need, but requires no
  economy/production/victory/AI loop. Just enough to prove the [A4](track-a-foundations.md) domain
  seam and [B6](deep-dives/distributed-simulation.md) partitioning are topology-general rather than
  RTS-shaped. Affects [A4](track-a-foundations.md), [B6](deep-dives/distributed-simulation.md).
- A2 strict-hash mode scope. Decision: same platform/build, best-effort, parallelism allowed.
  The opt-in strict-hash mode is scoped to a single platform + build and does not forbid intra-tick
  parallelism; it is therefore best-effort and may still diverge under nondeterministic parallel
  writes (consistent with the relaxed-determinism Decision). It is a finer-grained debugging
  signature that approaches bit-equality for same-platform/same-build single runs, not a guarantee of
  cross-platform or under-parallelism bit-exactness. The [A2](track-a-foundations.md) milestone is
  softened accordingly: no unconditional "reproduces bit-exactly" promise. Affects
  [A2](track-a-foundations.md).
- C5 external-model tooling. Decision: extend ReplaySketch, no dedicated viewer. External-model
  import/preview/validation tooling ([C5](track-c-external-assets.md)) extends ReplaySketch rather
  than spawning a separate viewer, per the no-fragmentation tooling convention (ReplaySketch is the
  primary tool). The "or a dedicated viewer" option is removed from C5. Affects
  [C5](track-c-external-assets.md).
- Save/load (StatePersister) reach. Decision: single-node only. The single `StatePersister`
  save/persist path is required to work only for single-node runs; it is not required to serialize
  sharded/distributed ([B6](deep-dives/distributed-simulation.md)) state. Distributed runs rely on the
  recorded snapshot/delta stream (the relaxed-determinism recording) for analysis and replay, not on
  `StatePersister`. The "one save/persist path" in the Simulation-domain scope Decision means one path
  per node, not one path spanning shards. Headless single-node runs may still round-trip save/load;
  cross-shard save/load is out of scope unless a later experiment needs it. Affects
  [A1](deep-dives/headless-host.md), [B3](track-b-scale.md),
  [B6](deep-dives/distributed-simulation.md).
- Non-SAGE gameplay mechanics scope. Decision: hosted as new Track D content on the existing
  RTS domain, not a new Simulation Domain Model. Age of Empires/Empire Earth style tech trees and
  era/age progression are new gameplay content and mechanism (a generalized prerequisite graph
  plus a new era state machine), added by extending the existing `GameObject`/module/`ObjectDefinition`
  system per the domain-scope Decision above: they are not different enough in kind (entities,
  interactions, topology) to justify a second Simulation Domain Model implementation. External
  content (models, INI overrides) for this track is ingested via the standalone
  [ModCollector tool](plans/mod-collector.md) (download/stage community mod archives into `.big`
  files) plus Track C's import abstraction (C1). ModCollector is a genuine prerequisite for
  Track D phase D3 onward, but D1/D2 (the tech-tree/era mechanism itself) do not need it and can
  be validated against existing built-in content first. Affects
  [Track D](track-d-gameplay-expansion.md),
  [deep-dives/tech-tree-era-progression.md](deep-dives/tech-tree-era-progression.md).
