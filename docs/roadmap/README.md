# Large-Scale RTS Simulation & External Asset Roadmap

> Living roadmap. Long-term plan for evolving OpenSAGE into an extensible framework for
> simulation-optimization experiments — a base on which to plug in and measure various ideas
> for scaling RTS simulation. It has no fixed product goal; the only fixed aims are
> scalability and flexibility. Specific capabilities (larger maps, distributed sim,
> variable fidelity, external assets, …) are experiments plugged into the framework, not
> destinations.
>
> This roadmap is split across several linked documents (see [Document map](#document-map)).
> Each phase has a Status (`Not started` / `In progress` / `Done` / `Revised`). When a
> phase grows its own detailed design, add a deep-dive under [`deep-dives/`](deep-dives/) and
> link it from the phase.
>
> Audience: human — start here for a project overview.

---

## Vision

The overarching aim is a framework for plugging in and measuring simulation-optimization
experiments. There is no fixed end-state — success is measured by how easily new scaling
ideas can be tried, swapped, and compared. Everything below is in service of two fixed aims:

- Scalability — push the simulation far beyond a single playable battle (more units,
  larger maps, headless/offline batch runs, distributed simulation, reduced per-unit cost).
- Flexibility — make techniques pluggable and interchangeable behind stable seams, so any
  given approach (a pathfinder, a fidelity policy, a partitioning scheme, a model format) can be
  replaced by an experiment without rewriting the engine.

The following are the vague long-term goals of this framework:

1. Arbitrarily large simulations — scale toward thousands–millions of entities, larger maps,
   headless/offline batch simulation, and distributed simulation across processes/machines. The
   first committed waypoint is 10k units at interactive rate (~30 fps) on one machine (see the
   [near-term scale anchor Decision](open-questions.md)); larger figures stay directional.
2. External asset incorporation — let units use models and animations authored outside the
   Westwood `.w3d` ecosystem, via a format-agnostic import abstraction.

> These goals are vehicles for experimentation, not the point in themselves. New goals can
> be added as the framework matures. The simulation domain itself is pluggable (see the
> [Simulation Domain Model seam](track-a-foundations.md)): RTS is the first domain and a
> transport/graph domain is the first non-RTS validation target, with other models (city
> builders, agent-based sims) hosted by extending the same `GameObject`/module system.

> Prerequisite — a playable baseline first. None of the above is worth scaling until the
> reference game runs end-to-end. [Track P — Playable Baseline](track-p-playable-baseline.md)
> is the hard prerequisite: a full Generals/Zero Hour skirmish must be playable build → fight →
> win/lose before the headless host, benchmarks, and experiment seams have a correct match loop
> to scale and measure. Track A/B/C work is gated on it.

### Guiding principles

- Framework first: optimization techniques are pluggable experiments. Prefer stable seams
  with swappable implementations (pathfinding, fidelity policy, partitioning, model import,
  storage layout) over hard-wiring any single approach. Each technique must be replaceable and
  independently measurable.
- Measure everything — robust benchmarking utilities are a first-class requirement. Because
  the goal is experimentation, every technique is evaluated against benchmarks
  ([B1](track-b-scale.md)) and inspected with the
  [result replayer/visualizer](deep-dives/visualization.md). The benchmarking utilities are
  themselves a deliverable, not ad-hoc scripts: repeatable scenarios, statistically rigorous
  results (multiple runs, variance/confidence reported, warmup discarded), regression detection
  against stored baselines, machine-readable output, and a reusable harness shared by every
  track. Cost/accuracy trade-offs are first-class outputs.
- Authoring & inspection tools scale with the simulation. The tools that create and
  inspect scenarios — WorldBuilder (maps + initial object placement) and
  [ReplaySketch](deep-dives/visualization.md) (command authoring, replay, visualization) —
  must handle the same magnitudes (entity counts, map sizes, command volumes) as the simulations
  they configure. You cannot author or analyze a million-entity experiment with tooling that
  assumes hundreds, so the scenario tooling sits on the scaling critical path, not beside it:
  WorldBuilder authoring scales under [B4a](track-b-scale.md) (with WorldBuilder baseline
  established first under [A5-6](deep-dives/visualization.md) — an early Track A sub-phase
  that does not depend on A1/A4), ReplaySketch/visualizer under
  [A5](deep-dives/visualization.md) and [P0](track-p-playable-baseline.md), reusing the same
  streaming/LOD/instancing ([B4a](deep-dives/larger-maps.md)/[B5](track-b-scale.md)) and
  procedural-generation techniques as the engine.
- Domain-agnostic where it's cheap. The simulation domain (entities, interactions,
  topology, metrics) is a pluggable seam, so the scale infrastructure — the
  [FTRT headless host](deep-dives/headless-host.md) and
  [distributed sim](deep-dives/distributed-simulation.md) — is not hard-wired to RTS and can host
  other simulation models. RTS is the reference domain, not a baked-in assumption.
- Scale and extensibility win over original-engine fidelity when they conflict. Reading
  original SAGE data remains valuable (and we should not break it gratuitously), but it is not
  allowed to block the architectural changes scale requires. Note the
  [relaxed-determinism Decision](open-questions.md) explicitly trades bit-exact, re-simulatable
  replays for parallel scale — recorded state, not input re-simulation, backs run analysis.
- Separate "simulation" from "presentation" cleanly. Headless and distributed scenarios
  require a logic core that runs with no renderer, audio, or window.
- Reproducibility for comparison, not bit-exactness. Per the
  [relaxed-determinism Decision](open-questions.md), the framework targets runs that are stable
  enough to compare experiments (seeded, re-runnable, metric/invariant-stable) rather than
  bit-identical. This buys aggressive intra-tick parallelism (with explicit conflict resolution)
  in exchange for giving up bit-exact replays.
- Incremental, always-shippable. Each phase should leave the engine building and the
  existing games playable.
- Single-operator scope. The framework is a personal research tool for one operator for the
  foreseeable future (see the [audience Decision](open-questions.md)). Tooling and run results
  target that operator — not external users — so no multi-user robustness, third-party
  reproducibility, or distribution hardening is a goal until that stance is revisited.

---

## Document map

### Audience guide

The roadmap docs divide into two audiences. Knowing which is which tells you what to read first
and what to skip.

| Audience | What to read | What to skip |
|---|---|---|
| Human (strategic overview, progress tracking, design intent) | `README.md`, `roadmap-tree.md`, `track-*.md`, `open-questions.md`, `implementation-checklist.md`, `deep-dives/` | `agent/`, `implementation/`, `agent/current-state.md`, `agent/MAINTAINING.md` |
| Agent (automated planning/execution, codebase context required) | `agent/`, `implementation/`, `agent/current-state.md`, `agent/MAINTAINING.md` | Track docs and deep dives provide needed context |

`seams.md` serves both: the overview and diagram sections are human-readable; the registry
detail and file citations are agent context. A split into a human overview and an agent registry
is planned.

The deep-dives and implementation plans are human-facing. Each deep-dive has code-grounding
content (file:line references) in a clearly marked appendix at the bottom; the sections above it
describe design intent without requiring codebase context. Each implementation plan has a `---`
divider separating the human-readable problem/design/approach from the agent-context file and test
details below it.

### All documents

| Document | Audience | Contents |
|---|---|---|
| [`agent/current-state.md`](agent/current-state.md) | agent | Snapshot of the architecture this roadmap builds on (cited). |
| [`agent/MAINTAINING.md`](agent/MAINTAINING.md) | agent | Planning-agent protocol: the single checklist for propagating a plan change across all parallel docs (with a change-type matrix + consistency invariants). |
| [`roadmap-tree.md`](roadmap-tree.md) | human | Visual tree of the whole roadmap: tracks → phases → sub-phases hierarchy + the branching dependency flow. |
| [`seams.md`](seams.md) | human + agent | Definition of a "seam" and the maintained registry of all framework seams (structural + A4 experiment seams). Pending split into a human overview and an agent registry. |
| [`track-p-playable-baseline.md`](track-p-playable-baseline.md) | human | Prerequisite track: make the reference game (Generals/ZH) a fully playable end-to-end skirmish (setup, AI, economy, production, combat, win/lose). Gates Tracks A/B/C. |
| [`track-a-foundations.md`](track-a-foundations.md) | human | Shared prerequisites: headless host, reproducibility & statistical stability, sim/presentation seam, experiment framework, scenario authoring & visualization. |
| [`track-b-scale.md`](track-b-scale.md) | human | Scaling work: benchmarks, allocation cleanup, parallelism, spatial/pathing, rendering, distributed sim. |
| [`track-c-external-assets.md`](track-c-external-assets.md) | human | External model/animation import: abstraction, retargeting, materials, first format, tooling. |
| [`open-questions.md`](open-questions.md) | human | Decision log: every item is now a settled Decision (rationale + affected phase). No questions currently open; add new ones here and convert to a Decision once settled. |
| [`implementation-checklist.md`](implementation-checklist.md) | human | Single granular change list: every concrete implementation change across the roadmap, ordered by dependency, checked off as it lands. |
| [`agent/`](agent/) | agent | Agent-focused companion: how a low-effort automated agent picks up small, bounded slices of the roadmap (task cards + workflow) without re-reasoning the whole design. |
| [`implementation/`](implementation/) | human | Per-sub-phase plans: problem, design rationale, and approach above the divider; file:line agent-context details below the divider. |
| [`deep-dives/`](deep-dives/) | human | Per-phase detailed designs. Code-grounding content is in a clearly marked appendix at the bottom of each doc; the sections above are readable without codebase context. |

### Deep dives

| Deep dive | Expands |
|---|---|
| [`deep-dives/playable-baseline-harness.md`](deep-dives/playable-baseline-harness.md) | Track P phase P0 — ReplaySketch arbitrary-command harness (open command model + headless assertion layer) and the agent validation playbook (MAUI testability seams + `ReplaySketch.Maui/AGENTS.md`). |
| [`deep-dives/skirmish-setup.md`](deep-dives/skirmish-setup.md) | Track P phase P1 — Skirmish setup & player relationships. |
| [`deep-dives/skirmish-ai.md`](deep-dives/skirmish-ai.md) | Track P phases P2 — Skirmish AI opponent (+ P5 construction/production mechanism). |
| [`deep-dives/victory-defeat.md`](deep-dives/victory-defeat.md) | Track P phase P3 — Victory & defeat conditions (queryable `GameOutcome`, all six match end states). |
| [`deep-dives/economy-production-combat.md`](deep-dives/economy-production-combat.md) | Track P phases P4 — Economy loop & P6 — Combat & command verification. |
| [`deep-dives/unit-contract-validation.md`](deep-dives/unit-contract-validation.md) | Track P phase P6b — Per-archetype unit-contract validation (infantry/vehicle/aircraft/builder/support/superweapon full contracts). |
| [`deep-dives/headless-host.md`](deep-dives/headless-host.md) | Track A phase A1 — Headless simulation host. |
| [`deep-dives/visualization.md`](deep-dives/visualization.md) | Track A phase A5 — Scenario authoring & visualization (scenario setup, sweeps, result replayer/visualizer, divergence localization, authoring/visualization at scale). |
| [`deep-dives/larger-maps.md`](deep-dives/larger-maps.md) | Track B phase B4a — Larger maps (via WorldBuilder) (terrain scaling + large-population scenario authoring). |
| [`deep-dives/distributed-simulation.md`](deep-dives/distributed-simulation.md) | Track B phase B6 — Distributed simulation (spatial sharding). |
| [`deep-dives/variable-fidelity.md`](deep-dives/variable-fidelity.md) | Track B phase B7 — Variable simulation fidelity (scaling toolkit). |

---

## Tracks at a glance

The roadmap is organized into two parallel tracks (Scale, External Assets) plus a shared
Foundations track — all gated on a playable-baseline prerequisite track. Phases within a track
are roughly ordered; cross-track dependencies are noted in each track doc.

- [Track P — Playable Baseline](track-p-playable-baseline.md) (prerequisite): P0 ReplaySketch
  arbitrary-command harness · P1 skirmish setup & player relationships · P2 skirmish AI opponent ·
  P3 victory/defeat conditions · P4 economy loop · P5 construction & production · P6 combat &
  command · P6b unit-contract validation · P7 end-to-end skirmish validation. Gates all other tracks.
- [Track A — Foundations](track-a-foundations.md): A1 headless/FTRT host · A2 reproducibility &
  statistical stability · A3 simulation/presentation seam · A4 experiment framework seams & harness
  (incl. the Simulation Domain Model seam — RTS, city-builder, transport, …) · A5 scenario
  authoring & visualization (incl. authoring/visualization at scale — the ReplaySketch tooling
  scales with the sim).
- [Track B — Scale](track-b-scale.md): B1 profiling & benchmarking utilities · B2 allocation cleanup · B3
  parallel/data-oriented logic · B4 spatial & pathfinding scaling · B4a larger maps (+
  large-population scenario authoring in WorldBuilder) · B5
  rendering at scale · B6 distributed simulation · B7 variable simulation fidelity.
- [Track C — External Models & Animations](track-c-external-assets.md): C1 import
  abstraction · C2 skeleton/animation retargeting · C3 material mapping · C4 first concrete
  format (static + skinned) · C5 authoring/validation tooling · C6 extended features (deferred:
  morph targets, advanced PBR).

### Cross-track dependencies

- Track P gates everything. [A1 (headless host)](track-a-foundations.md) depends on
  P7 — a validated, terminating skirmish loop is the headless host's unit of work; A2/A4 and
  all of Tracks B/C inherit that prerequisite.
- B5 (instanced rendering) should account for Track C models so instancing is not
  W3D-specific.
- C1 and A3 both touch the presentation seam — coordinate so the import abstraction
  feeds the same runtime `Model`/`ModelInstance` types that the rendering refactor expects.

---

## Progress

Counts are against the [implementation checklist](implementation-checklist.md). Update when items are checked off.

### Track P — Playable Baseline

| Phase | Progress | Items |
|---|---|---|
| P0 — ReplaySketch harness | `███░░░░░░░` | 4 / 14 |
| P1 — Skirmish setup | `░░░░░░░░░░` | 0 / 7 |
| P2 — Skirmish AI | `░░░░░░░░░░` | 0 / 7 |
| P3 — Victory & defeat | `░░░░░░░░░░` | 0 / 9 |
| P4 — Economy loop | `░░░░░░░░░░` | 0 / 5 |
| P5 — Construction & production | `░░░░░░░░░░` | 0 / 5 |
| P6 — Combat & command | `░░░░░░░░░░` | 0 / 5 |
| P6b — Unit-contract validation | `░░░░░░░░░░` | 0 / 8 |
| P7 — End-to-end validation | `░░░░░░░░░░` | 0 / 6 |
| Track P total | `░░░░░░░░░░` | 4 / 66 |

### Tracks A / B / C (gated on Track P)

| Track | Progress | Items |
|---|---|---|
| Track A — Foundations | `░░░░░░░░░░` | 0 / 18 |
| Track B — Scale | `░░░░░░░░░░` | 0 / 12 |
| Track C — External Assets | `░░░░░░░░░░` | 0 / 6 |

---



> Iterating on the plan? Follow [`agent/MAINTAINING.md`](agent/MAINTAINING.md) — the single planning-agent
> protocol for propagating a change across all parallel documents (change-type matrix, ordered
> checklist, consistency invariants, validation). The bullets below are the quick summary.

- Update a phase's Status when it changes state, in its track document.
- Keep the [Progress](#progress) tables in sync — update the done/total counts and bar when checklist items are checked off.
- Keep the [implementation checklist](implementation-checklist.md) in sync — check off granular
  changes as they land and add finer items as a phase gains detail.
- When a phase gets a detailed design, add a doc under [`deep-dives/`](deep-dives/) and link it
  from the phase row (and from the [Deep dives](#deep-dives) table above).
- When [`agent/current-state.md`](agent/current-state.md) drifts from the code, refresh it — it is the
  basis for every phase's assumptions.
- Record significant decisions in [`open-questions.md`](open-questions.md) (move an item from
  "open question" to a noted decision, and update the affected phase).
- Keep this README's tables in sync when documents or deep dives are added or removed.
- Update the [visual roadmap tree](roadmap-tree.md) whenever a phase, sub-phase, or dependency is
  added, removed, or renamed (see its own
  [maintenance note](roadmap-tree.md#how-to-maintain-this-tree)).
- Update the [seam registry](seams.md) whenever a phase introduces a new pluggable seam or adds an
  implementation behind an existing one.
- When a phase is ready to be chipped at by cheap/automated agents, decompose it into task cards
  in the [`agent/`](agent/) (keep card Status and the phase Status in sync).
