# Open Questions

> Part of the [Large-Scale Simulation Roadmap](README.md). This document is the decision log.
> Each entry is a settled Decision with rationale and links back to the affected phase. No
> questions remain open; new items join here and become Decisions once settled.
>
> Audience: human reference for roadmap decisions.

- Determinism priority. Decision: determinism is not a roadmap requirement or a gate for
  parallelism, distribution, physics, AI, replay, or experiment tooling. Runs may diverge across
  executions, platforms, builds, thread schedules, and shard layouts. Validation uses per-run
  invariants, authored bounds, aggregate distributions, and performance statistics where useful.
  Seed controls remain available as an experiment input, but do not imply reproducibility.
  Within a tick, data-parallel systems may run concurrently and write freely; conflicting writes
  are reconciled through explicit intent/command buffers and authority rules for correctness.
  Consequences:
  - [A2](track-a-foundations.md) is statistical validation: its harness reports distributions and
    checks invariants without requiring same-seed agreement or state-hash parity.
  - [B3](track-b-scale.md) adopts the free-intra-tick + conflict-resolution model as its
    concrete parallelism design (read/modify via intent buffers, merge by rules).
  - [B6](deep-dives/b6-distributed-simulation.md) validates handoff/boundary sync by
    invariants such as no unit duplication/loss and conservation. Cross-shard conflicts use the
    same authority/resolution rules.
  - Recorded state snapshots/deltas, not input-only re-simulation, back the replayer. For
    diagnostic comparisons, A1-5 records and A5-4
    localizes via a tiered, hierarchical recording (aggregate metrics → per-region/entity
    signature tree → triggered per-entity detail) captured live; see
    [visualization.md §5](deep-dives/a5-visualization.md).
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
  [B6 deep dive](deep-dives/b6-distributed-simulation.md). Affects
  [B6](track-b-scale.md) and informs [A2](track-a-foundations.md)/[B3](track-b-scale.md).
- glTF scope for C4. Decision: static meshes + skinned skeletal animation only. The first
  production external format ([C4](track-c-external-assets.md)) covers the minimum needed to get
  externally-authored units, with mesh, skeleton, and gameplay-driven animation, into the engine.
  Morph targets (blendshapes) and advanced glTF PBR material extensions are deferred to
  [C6](track-c-external-assets.md) and added only if an experiment needs them. This keeps the
  first importer small and proves the C1 abstraction end-to-end as early as possible.
- C4 glTF importer architecture. Decision: use strict SharpGLTF.Core parsing behind a
  glTF-private adapter, then expose only C1-C3 engine-owned values. External resources resolve
  through the selected model's virtual root. One basis maps `(x, y, z)` to `(z, x, y)` across
  geometry, nodes, inverse binds, animation, and bounds, and one meter maps to approximately
  3.28084 foot-based world units. Scene nodes remain runtime bones for rigid attachments. The
  first skinning subset supports one skin and two meaningful influences, with inverse bind
  matrices added to the neutral skeleton.
  External-tool sessions select this importer explicitly without replacing shipped game
  definitions' single-importer strategy. Affects
  [C4](track-c-external-assets.md).
- Empire Earth II proof scope. Decision: [C4a](track-c-external-assets.md) is an additive,
  experimental second-format proof after C1, not a replacement for C4's production glTF
  importer. It loads the static NIF 10.1 War Galley and Twin-Engine Bomber models directly from
  an operator-supplied `graphics.zip`, bakes the Bomber's highest-detail LOD, preserves
  fixed-function values and source texture names, and renders untextured diffuse materials. KF
  animation, skinning, `.texcache` decoding, player-color patches, DDF integration, and older NIF
  versions remain outside the proof. No EE2 asset is committed.
- C1 import-seam shape. Decision: importers parse source files into CPU-only engine interchange
  values; engine-owned materializers validate those values and create runtime models, animations,
  materials, textures, and GPU resources. `OnDemandAssetLoadStrategy` carries the selected
  `IModelImporter` / `IAnimationImporter`; shipped games select internal W3D adapters through a
  public in-assembly factory, while
  `OnDemandAssetLoadStrategy.None` remains importer-free. Referenced skeleton loading remains
  behind `IModelImporter` and the existing hierarchy cache. Missing assets return null with one
  loader warning; malformed or unsupported source data throws a contextual exception. This keeps
  file formats out of runtime graphics types, makes imported data inspectable without a GPU, and
  gives C2/C3/C4 one stable boundary. Affects
  [Track C](track-c-external-assets.md) (C1).
- C2 retargeting scope. Decision: imported animation channels use either direct bone indices or
  exact case-insensitive bone names. Binding occurs once per `ModelAnimation` and target
  `ModelBoneHierarchy`, before `AnimationInstance` playback, and the immutable result is held in a
  weak-key cache by hierarchy identity. W3D retains direct-index behavior, including omitting
  out-of-range channels rather than failing the asset. C2 adds no INI alias syntax, heuristic
  matching, humanoid semantics, bind-pose compensation, or per-frame name lookup. Affects
  [Track C](track-c-external-assets.md) (C2).
- C3 material contract. Decision: core metallic-roughness data uses a typed, format-neutral
  payload on `ImportedMaterial`, not shader-property naming conventions. Typed texture bindings
  carry external or embedded image sources, UV set, sampler, role, and color-space semantics.
  Engine-owned materialization validates and decodes them, generates missing tangent bases when
  possible, and renders through one core PBR shader path. Advanced `KHR_materials_*` semantics
  remain C6. Affects [Track C](track-c-external-assets.md) (C3/C4/C6).
- C6 extension activation. Decision: morph targets and advanced PBR remain request-scoped
  experiments behind named feature gates that default off. Morph deltas and weight animation use
  primitive-target identities separate from vertices and bone channels, with morph-before-skin
  deformation. Advanced materials use typed optional families and normalized shader feature keys;
  transmission and volume semantics require an explicit renderer-pass experiment. Required
  unsupported semantics fail, while optional material extensions may be ignored with a warning
  only when core output remains complete. Affects
  [C6](track-c-external-assets.md).
- Large-map ceiling & compatibility. Decision: no size cap; no compatibility constraint.
  Maps scale arbitrarily (bounded by disk, not RAM/format), to be grown gradually for scaling
  experiments; OpenSAGE may extend/replace the `.map` format and need not stay loadable in the
  original engine. Makes chunked storage (B4a-5) a core requirement. See
  [B4a deep dive](deep-dives/b4a-larger-maps.md).
- Variable-fidelity consistency stance. Decision: fidelity is a function of replicated
  simulation state for anything shared or distributed; view-driven fidelity is allowed only for
  single-node, non-distributed experiments. Shared and distributed
  runs therefore derive fidelity inputs from replicated state so every shard agrees on each
  entity's fidelity level, never from a local camera or view. View-driven fidelity, such as LOD
  by camera distance, remains available for single-node experiments where no shard can disagree.
  Affects [B7](deep-dives/b7-variable-fidelity.md) and
  [A2](track-a-foundations.md)/[B6](deep-dives/b6-distributed-simulation.md).
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
    parts of the A4 seam, [B6](deep-dives/b6-distributed-simulation.md) partitioning, and A5-4
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
  - Affects [A4](track-a-foundations.md), [A1](deep-dives/a1-headless-host.md),
    [B6](deep-dives/b6-distributed-simulation.md), and [B3](track-b-scale.md).
- P0 harness: runtime object references. Decision: implicit auto-handles + opt-in names + query
  selectors. Authored commands in the [P0 harness](deep-dives/p0-playable-baseline-harness.md)
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
  [P0 harness](deep-dives/p0-playable-baseline-harness.md) is declarative JSON predicates (e.g.
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
  `OpenSage.Tools.ReplaySketch.Tests` plus the [P0 harness](deep-dives/p0-playable-baseline-harness.md)
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
  [P1-5](deep-dives/p1-skirmish-setup.md), owned by P1 and shared by both consumers. Affects
  [P1](track-p-playable-baseline.md), [P2](track-p-playable-baseline.md),
  [P3](track-p-playable-baseline.md).
- Skirmish AI actuation. Decision: emit `Order`s through `OrderProcessor`. The AI issues the
  same `Order`s a human would (`BuildObject`/`CreateUnit`/`ForceAttack…`) rather than calling engine
  APIs directly, so AI actions enter the recorded/replayed order stream and remain consistent with the
  [P0 harness](deep-dives/p0-playable-baseline-harness.md). Trade-off accepted: the AI must synthesize
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
  through `OrderProcessor` (the P2 actuation decision above), so the recorded/replayed order
  stream holds regardless of which policy runs. This is what unblocks a
  neural-network policy trained by running many seeded self-play matches (a population of
  weight-vector genomes, fitness scored off `GameOutcome`, evolved via selection/mutation across
  generations in an A4 experiment sweep). Design-only for now; implementation (interface
  extraction, a first NN policy, and the training harness) is follow-on work. Detailed design:
  [deep-dives/a4-pluggable-ai.md](deep-dives/a4-pluggable-ai.md). Affects
  [A4](track-a-foundations.md), [P2](track-p-playable-baseline.md).
- Decentralized hierarchical AI. Decision: generalize the A4 AI decision policy seam with a
  rank hierarchy (general, one per player; commander, one per squad; soldier, one per unit) of
  per-rank neural networks, rather than a single flat policy per player. This is an alternative
  policy family behind the same `IAIDecisionPolicy` seam, not a replacement for it: a
  `HierarchicalAIDecisionPolicy` adapter runs the ranks and still emits every order through
  `OrderProcessor`, so the actuation constraints already established for A4/P2 hold unchanged.
  Squads derive from the existing team/selection model; a squad's commander is
  promoted from its highest-value surviving member on commander death. Network complexity scales
  by rank (soldier smallest, general largest), not by total unit count. Design-only for now;
  implementation and a co-evolutionary training scheme are follow-on work. Detailed design:
  [deep-dives/a4-decentralized-hierarchical-ai.md](deep-dives/a4-decentralized-hierarchical-ai.md).
  Affects [A4](track-a-foundations.md).
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
- Geospatial map import source boundary. Decision: consume operator-supplied exports, not a live
  Google service. B4b accepts KML/KMZ plus georeferenced DEM and imagery raster files exported
  through Google Earth, Google Earth Engine, or another source the operator is authorized to use.
  It records source and projection metadata but does not authenticate to, scrape, or download from
  Google services. Repository tests use synthetic fixtures. This keeps the importer deterministic
  and service-independent while leaving source-data rights with the operator under the existing
  single-operator scope. Affects [B4b](track-b-scale.md).
- Non-RTS validation domain completeness. Decision: minimal partition-stress harness. The first
  non-RTS (transport/graph) domain (see the Simulation-domain scope Decision) is implemented only as
  far as needed to exercise graph-cut (non-spatial) partitioning and edge-coupled interaction/owner
  authority, not a complete, terminating, Track-P-grade scenario. It instantiates graph entities,
  runs them headlessly, and emits the partition/boundary metrics A4/B6 need, but requires no
  economy/production/victory/AI loop. Just enough to prove the [A4](track-a-foundations.md) domain
  seam and [B6](deep-dives/b6-distributed-simulation.md) partitioning are topology-general rather than
  RTS-shaped. Affects [A4](track-a-foundations.md), [B6](deep-dives/b6-distributed-simulation.md).
- C5 external-model tooling. Decision: extend ReplaySketch with a dedicated Model tab, no
  dedicated viewer. ReplaySketch Core owns session state, stable diagnostics, animation pairing,
  preview eligibility, and reports; MAUI owns presentation. TestHarness performs one isolated
  graphics-backed capture per versioned request and disposes GPU resources on exit. External
  roots never layer into game assets, and transforms remain preview-only. This preserves the
  no-fragmentation tooling convention and keeps graphics out of the external-model Core path.
  Affects
  [C5](track-c-external-assets.md).
- Save/load (StatePersister) reach. Decision: single-node only. The single `StatePersister`
  save/persist path is required to work only for single-node runs; it is not required to serialize
  sharded/distributed ([B6](deep-dives/b6-distributed-simulation.md)) state. Distributed runs rely on the
  recorded snapshot/delta stream for analysis and replay, not on
  `StatePersister`. The "one save/persist path" in the Simulation-domain scope Decision means one path
  per node, not one path spanning shards. Headless single-node runs may still round-trip save/load;
  cross-shard save/load is out of scope unless a later experiment needs it. Affects
  [A1](deep-dives/a1-headless-host.md), [B3](track-b-scale.md),
  [B6](deep-dives/b6-distributed-simulation.md).
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
  [deep-dives/d1-d2-tech-tree-era-progression.md](deep-dives/d1-d2-tech-tree-era-progression.md).
- Object physics, deformable terrain & destruction scope, Decision: own track (Track F), not folded
  into an existing track; destruction is physics-based, not model-swap. Runtime terrain deformation
  (craters/scorch marks) does not exist in original SAGE, and Track F's destruction (F4a/F5)
  replaces the canned model-swap approach (rubble/damage-state model swaps, already covered under
  P6-2/P6-3) with rigid-body physics simulation (debris/structural collapse via a new pluggable
  physics seam). Both are new fidelity beyond what P6-2/P6-3 verify, so neither is gated by Track P
  playable-baseline fidelity. The track is split out because it cuts across terrain storage
  ([B4a](deep-dives/b4a-larger-maps.md)), rendering ([B5](track-b-scale.md)), a new physics substrate,
  combat ([P6](deep-dives/p4-p6-economy-production-combat.md)), and save/replay rather than fitting
  inside any single existing track; the same
  reasoning that split gameplay content out as Track D. Named "Track F" (not "Track D"/"Track E")
  to avoid collisions with two pending unmerged branches: `docs/track-d-gameplay-expansion`
  (Gameplay Content Expansion) and `docs/track-e-naval-warfare` (Naval Warfare). Since superseded
  by events: both branches merged (PRs #5 and #6), so Tracks D and E are on `main` and the letter
  assignment stands on that basis rather than on pending work. The physics
  engine/library is an open candidate, settled when F4 starts, mirroring how Track C left the
  first external model format ([C4](track-c-external-assets.md)) as a candidate. Since settled:
  BepuPhysics2, see the object physics engine Decision below. Affects
  [Track F](track-f-terrain-destruction.md).
- Object physics engine, Decision: BepuPhysics2, consumed as NuGet packages `BepuPhysics` +
  `BepuUtilities` 2.4.0 (Apache-2.0, GPLv3-compatible) behind an engine-internal object physics
  seam, settling the F4 candidate. SAGE behavioral fidelity is no longer binding for physics.
  Every instantiated `GameObject` resolves through one engine-level participation policy:
  dynamic, kinematic, static, sensor, or no physics. Physical units, projectiles, loose objects,
  structures, bridges, props, and watercraft use Bepu as applicable; drawable-only effects,
  script markers, and other objects with no physical role remain outside the solver. Bepu owns
  solid contact generation/response while `PartitionCellManager` retains gameplay queries and
  non-physical regions. Each `GameEngine` receives one backend through injected configuration;
  the proof-of-concept process-global selector is retired. Migration is incremental by category
  inside Bepu worlds, with the current `PhysicsBehavior` Euler integrator retained as the reference
  backend. Bepu becomes the default only after the F4a cross-game classification and scenario
  matrix passes.
  Gameplay-decisive reads may consume contact state and are validated by invariants and loose
  authored bounds. Design: [physics-seam plan](plans/physics-seam.md). Affects
  [Track F](track-f-terrain-destruction.md), [Track E](track-e-naval-warfare.md),
  [Track B](track-b-scale.md), and [Track H](track-h-storage-formats.md).
- Rendering clarity & image quality scope, Decision: own track (Track G), not folded into
  [B5 rendering at scale](track-b-scale.md), and not gated on Track P. The current renderer is
  faithful to the original game but grainy on high-resolution displays, with a screen-door pattern
  on alpha-tested geometry. The causes span a genuine correctness bug (no
  `SDL_WINDOW_ALLOW_HIGHDPI`, so the swapchain is sized from logical points and the frame is
  rendered below native resolution and resampled), plus absent capability across antialiasing,
  cutout filtering, colour space, texture filtering, shadow provisioning, and 2D scaling. B5 is
  about throughput at entity scale, so image quality would be a grab-bag inside it, the same
  reasoning that split Tracks D, E, and F out. Track G touches presentation only and changes no
  simulation behaviour, so unlike Tracks A/B/C it does not need a playable skirmish loop first.
  Named "Track G" because D, E, and F are all merged on `main`
  ([Track D](track-d-gameplay-expansion.md), [Track E](track-e-naval-warfare.md),
  [Track F](track-f-terrain-destruction.md)). Affects
  [Track G](track-g-rendering-clarity.md).
- Original-look compatibility under Track G, Decision: two presets, `Faithful` and `Enhanced`,
  with `Enhanced` as the default and the target of future work. `Faithful` pins sample count 1,
  the hard `0x60` alpha-test cutoff, gamma-space shading, 4x anisotropy, and a single 1024²
  shadow cascade, reproducing the pre-track output exactly; the G1 high-DPI fix applies to both
  because it is a correctness bug rather than a look change. Presets are a settings layer rather
  than an [experiment seam](seams.md): they select fixed pipeline state, not swappable
  implementations measured by the A4 harness, so they stay out of that harness. The alternative,
  a single unconditional visual upgrade, was rejected because reproducing the original look is a
  standing requirement, and the alternative of   a full per-feature settings matrix without presets
  was rejected as untestable: G3 golden frames are captured per preset. Affects
  [Track G](track-g-rendering-clarity.md).
- Realistic proportions scope. Decision: presentation-only scale correction in Track G (G10),
  grounded in a measured reference scale; weapon ranges and collision geometry stay untouched.
  Measured against installed assets (INI `Geometry` cross-checked with W3D mesh bounds, FX meshes
  excluded), infantry, ground vehicles, and civilian/faction buildings are already mutually
  proportionate at roughly 5 world units per meter (terrain cell 10 units ≈ 2 m); aircraft models
  are the offenders, undersized roughly 2-2.5x versus real counterparts (MiG ≈ 6.6 m in-game
  versus ≈ 17.3 m real). G10 documents the reference scale and adds an opt-in visual model-scale
  correction for the aircraft class behind the G2 settings layer, in the spirit of the `Enhanced`
  preset. Attack-range compression (≈ 100x, e.g. `CrusaderTankGun AttackRange = 150` ≈ 30 m
  versus 2.5+ km real) is deliberate gameplay and map-scale data rather than presentation, so it
  is explicitly out of scope, as are `Geometry` collision boxes (selection/pathing consumers such
  as `Scene25D.GetBoundingSphere`). Chosen over rescaling assets (mutates original data) and over
  simulation-side scaling (would change gameplay and break the A3 seam). Affects
  [Track G](track-g-rendering-clarity.md) (G10).
- Map & unit storage-format scope. Decision: own track (Track H), scoped to on-disk persistence
  and schema design, not runtime performance. A new storage system for maps and unit/object
  definitions is a genuine gap: no roadmap phase designs a successor on-disk format for the
  `.map` file or per-game INI `ObjectDefinition` data. This is deliberately kept separate from
  [B3](track-b-scale.md) (data-oriented runtime layout for unit/`GameObject` state) and
  [B4a-5](deep-dives/b4a-larger-maps.md) (chunked terrain runtime storage for scale), which
  already own the in-memory/streaming side of "storage" for units and maps respectively. Track H
  instead answers a different question: what does content look like on disk, how does its schema
  version and evolve, and how do maps and object definitions get authored, converted, and
  validated against that schema. B4a-5's "extend or replace the `.map` format" milestone stays
  scale-driven (chunking so the whole grid need not be resident); Track H's H3 designs the
  general-purpose container/versioning envelope and reserves a terrain section for B4a-5's
  chunk representation, so the two efforts converge on one map format rather than competing.
  Split out as its own track for the same reason as Tracks D/E/F/G: it cuts across terrain
  storage (B4a-5), the external-asset import pattern ([C1](track-c-external-assets.md)), the
  modding/content pipeline ([Track D](track-d-gameplay-expansion.md),
  [ModCollector](plans/mod-collector.md)), and authoring tooling (WorldBuilder), rather than
  fitting inside any single existing track. Named "Track H" because A–G are already assigned on
  `main`. Its hard dependency is Track P (P7); coordination with F4a concerns optional
  engine-level physics fields and does not gate the format seam. Affects
  [Track H](track-h-storage-formats.md).
- HUD rework scope. Decision: replacing the pre-rendered HUD is in scope for G9, as an additional
  work item rather than a new track or phase. G9's original scope (native-resolution 2D,
  logical-point layout, better upscaling) makes the legacy 800x600 WND/APT bitmaps look as good as
  upscaling allows, but the HUD remains pre-rendered art. G9-3 replaces the control bar's bitmaps
  with dynamically drawn equivalents (vector/procedural panels, buttons, frames; live text), which
  is the genuine resolution independence the phase is named for. Chosen over a separate UI track
  because the work shares G9's whole substrate (the 2D layer, logical-point layout, the G2 preset
  switch, and the G3 golden frames that gate it) and has no scope beyond rendering clarity; a
  UI-framework or UX redesign stays out of scope and would be new roadmap material if ever
  wanted. Gated behind the `Enhanced` preset with `Faithful` pixel-identical to the pre-rework
  capture, consistent with the rendering-clarity scope Decision. Affects
  [Track G](track-g-rendering-clarity.md) (G9).
- Neural-network AI policy v1 action and feature scope. Decision: v1 ships a 9-feature
  input, 9-8-5 tanh/softmax network over a five-action catalog (none, supply build, next
  structure, combat unit, attack) with descoped gather reassignment and no policy RNG, per
  [neural-network-ai-policy.md](deep-dives/neural-network-ai-policy.md). Expanding the
  catalog or feature vector (enemy economy estimates, spatial targeting, special powers) is
  deferred until the evolutionary training harness produces fitness results that justify
  the added genome size; until then the v1 shapes are the stable genome contract.
  Affects [Track A](track-a-foundations.md) (A4).
- RTS topology description scope. Decision: the RTS domain adapter returns an empty
  <c>DescribeTopology</c> result for now; RTS visualization is position-driven and no current
  consumer (A5 or B6) needs occupied-cell adjacency. Revisit when one does; the contract
  already carries the attachment point. Affects
  [Track A](track-a-foundations.md) (A4-1).
- Per-rank hierarchical fitness signal. Decision: v1 hierarchical co-evolution uses shared
  outcome credit only (every rank in a match receives the flat scorer's match fitness). Per-rank
  shaped fitness, such as a commander's squad survival rate, requires per-rank T0 run-output
  metrics that do not exist yet; when they land, the co-evolution contract grows per-rank credit
  assignment without changing the schedule. Affects
  [Track A](track-a-foundations.md) (A4-3f).
