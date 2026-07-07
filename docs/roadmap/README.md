# Large-Scale RTS Simulation & External Asset Roadmap

> Living roadmap for evolving OpenSAGE into an extensible framework for
> simulation-optimization experiments. The fixed aims are scalability and flexibility.
> Larger maps, distributed sim, variable fidelity, external assets, and similar capabilities
> are experiments enabled by the framework, not end goals.
>
> This roadmap is split across linked documents; see [Document map](#document-map). Each phase
> has a Status (`Not started` / `In progress` / `Done` / `Revised`). Add deep dives under
> [`deep-dives/`](deep-dives/) and link them from the owning phase.
>
> Audience: human — start here for a project overview.

---

## Vision

The aim is a framework for plugging in and measuring simulation-optimization experiments.
There is no fixed end-state. Success is how easily new scaling ideas can be tried, swapped,
and compared. Everything below serves two fixed aims:

- Scalability — push the simulation far beyond a single playable battle (more units,
  larger maps, headless/offline batch runs, distributed simulation, reduced per-unit cost).
- Flexibility — make techniques pluggable and interchangeable behind stable seams, so any
  given approach (a pathfinder, a fidelity policy, a partitioning scheme, a model format) can be
  replaced by an experiment without rewriting the engine.

Long-term goals:

1. Arbitrarily large simulations — scale toward thousands–millions of entities, larger maps,
   headless/offline batch simulation, and distributed simulation across processes/machines. The
   first committed waypoint is 10k units at interactive rate (~30 fps) on one machine (see the
   [near-term scale anchor Decision](open-questions.md)); larger figures stay directional.
2. External asset incorporation — let units use models and animations authored outside the
   Westwood `.w3d` ecosystem, via a format-agnostic import abstraction.

> These goals are experiments, not the point in themselves. New goals can be added later. The
> simulation domain is pluggable; see the [Simulation Domain Model seam](track-a-foundations.md).
> RTS is the first domain, transport/graph is the first non-RTS validation target, and other
> models such as city builders and agent-based sims extend the same `GameObject`/module system.

> Prerequisite: a playable baseline first. [Track P — Playable Baseline](track-p-playable-baseline.md)
> is the hard prerequisite. A full Generals/Zero Hour skirmish must run build → fight → win/lose
> before the headless host, benchmarks, and experiment seams have a correct match loop to scale
> and measure. Track A/B/C work is gated on it.

### Guiding principles

- Framework first. Optimization techniques are experiments behind stable seams with swappable
  implementations for pathfinding, fidelity policy, partitioning, model import, and storage
  layout. Each technique must be replaceable and independently measurable.
- Measure everything. Benchmarking utilities are a deliverable, not ad-hoc scripts. Every
  technique is evaluated against [B1](track-b-scale.md) benchmarks and inspected with the
  [result replayer/visualizer](deep-dives/visualization.md). The shared harness must support
  repeatable scenarios, multiple runs, variance/confidence reporting, discarded warmup,
  regression detection against stored baselines, machine-readable output, and cost/accuracy
  trade-offs.
- Authoring and inspection tools scale with the simulation. WorldBuilder and
  [ReplaySketch](deep-dives/visualization.md) must handle the same entity counts, map sizes,
  and command volumes as the simulations they configure. WorldBuilder authoring scales under
  [B4a](track-b-scale.md), after the [A5-6](deep-dives/visualization.md) baseline lands.
  ReplaySketch and the visualizer scale under [A5](deep-dives/visualization.md) and
  [P0](track-p-playable-baseline.md), reusing the engine's streaming, LOD, instancing,
  and procedural-generation work from [B4a](deep-dives/larger-maps.md) and
  [B5](track-b-scale.md).
- Domain-agnostic where it is cheap. The simulation domain is a pluggable seam, so the
  [FTRT headless host](deep-dives/headless-host.md) and
  [distributed sim](deep-dives/distributed-simulation.md) are not hard-wired to RTS. RTS is the
  reference domain, not a baked-in assumption.
- Scale and extensibility win over original-engine fidelity when they conflict. Reading
  original SAGE data remains valuable, but it must not block the architectural changes scale
  requires. The [relaxed-determinism Decision](open-questions.md) trades bit-exact,
  re-simulatable replays for parallel scale; recorded state, not input re-simulation, backs run
  analysis.
- Separate simulation from presentation cleanly. Headless and distributed scenarios need a
  logic core that runs with no renderer, audio, or window.
- Reproducibility is for comparison, not bit-exactness. Runs must be seeded, re-runnable, and
  metric/invariant-stable enough to compare experiments. The trade-off is aggressive intra-tick
  parallelism with explicit conflict resolution instead of bit-exact replays.
- Incremental, always-shippable. Each phase should leave the engine building and the existing
  games playable.
- Single-operator scope. The framework is a personal research tool for one operator for the
  foreseeable future. Tooling and run results target that operator, not external users, so
  multi-user robustness, third-party reproducibility, and distribution hardening stay out of
  scope until that stance changes.

---

## Document map

### Audience guide

The roadmap docs divide into two audiences. Use this to decide what to read first.

| Audience | What to read | What to skip |
|---|---|---|
| Human (strategic overview, progress tracking, design intent) | `README.md`, `roadmap-tree.md`, `track-*.md`, `open-questions.md`, `implementation-checklist.md`, `deep-dives/` | `agent/`, `implementation/`, `agent/current-state.md`, `agent/MAINTAINING.md` |
| Agent (automated planning/execution, codebase context required) | `agent/`, `implementation/`, `agent/current-state.md`, `agent/MAINTAINING.md` | Track docs and deep dives provide needed context |

`seams.md` serves both audiences: the overview and diagrams are human-facing, while the
registry detail and file citations are agent context. A split into separate overview and registry
docs is planned.

Deep dives and implementation plans are human-facing. Deep dives keep code-grounding file:line
references in a marked appendix at the bottom. Implementation plans use a `---` divider between
human-readable problem/design/approach sections and agent-context file and test details.

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
| [`track-d-gameplay-expansion.md`](track-d-gameplay-expansion.md) | human | Gameplay content expansion beyond the built-in SAGE titles: tech-tree/prerequisite-graph generalization, era/age progression, external content ingestion (ModCollector + Track C), a reference non-SAGE scenario. Gated on Track P; D3 onward also gated on Track C/ModCollector. |
| [`track-e-naval-warfare.md`](track-e-naval-warfare.md) | human | Naval warfare: finishes the stubbed water/ground pathfinding layer and adds ship combat/production, validated against BFME2's existing ship content. Gated on Track P only. |
| [`plans/mod-collector.md`](plans/mod-collector.md) | human | Design for the standalone `OpenSage.Tools.ModCollector` CLI: downloads/stages community mod archives into `.big` files, a prerequisite for Track D phase D3. |
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
| [`deep-dives/pluggable-ai.md`](deep-dives/pluggable-ai.md) | Track A phase A4 — Pluggable AI decision policy seam (rule-based reference implementation from P2, unblocking a neural-network policy trained via evolved seeded weight populations). |
| [`deep-dives/visualization.md`](deep-dives/visualization.md) | Track A phase A5 — Scenario authoring & visualization (scenario setup, sweeps, result replayer/visualizer, divergence localization, authoring/visualization at scale). |
| [`deep-dives/larger-maps.md`](deep-dives/larger-maps.md) | Track B phase B4a — Larger maps (via WorldBuilder) (terrain scaling + large-population scenario authoring). |
| [`deep-dives/distributed-simulation.md`](deep-dives/distributed-simulation.md) | Track B phase B6 — Distributed simulation (spatial sharding). |
| [`deep-dives/variable-fidelity.md`](deep-dives/variable-fidelity.md) | Track B phase B7 — Variable simulation fidelity (scaling toolkit). |
| [`deep-dives/tech-tree-era-progression.md`](deep-dives/tech-tree-era-progression.md) | Track D phases D1/D2 — Tech-tree/prerequisite-graph generalization and era/age progression. |
| [`deep-dives/naval-warfare.md`](deep-dives/naval-warfare.md) | Track E phases E1/E2 — Water/ground terrain passability and ship combat. |

---

## Tracks at a glance

The roadmap has a playable-baseline prerequisite track, a shared Foundations track, and two
parallel tracks for Scale and External Assets. Phases within a track are roughly ordered;
cross-track dependencies are noted in each track doc.

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
- [Track D — Gameplay Content Expansion](track-d-gameplay-expansion.md): D1 tech-tree/
  prerequisite-graph generalization · D2 era/age progression mechanic · D3 external content
  ingestion (ModCollector + Track C) · D4 reference non-SAGE scenario. Depends on Track P
  (D1/D2) and Track C/ModCollector (D3/D4).
- [Track E — Naval Warfare](track-e-naval-warfare.md): E1 water/ground terrain passability ·
  E2 ship combat · E3 naval production (`PORT`) · E4 reference naval scenario. Depends on Track
  P only.

### Cross-track dependencies

- Track P gates everything. [A1 (headless host)](track-a-foundations.md) depends on
  P7 — a validated, terminating skirmish loop is the headless host's unit of work; A2/A4 and
  all of Tracks B/C inherit that prerequisite.
- B5 (instanced rendering) should account for Track C models so instancing is not
  W3D-specific.
- C1 and A3 both touch the presentation seam — coordinate so the import abstraction
  feeds the same runtime `Model`/`ModelInstance` types that the rendering refactor expects.
- Track D (D1/D2) depends only on Track P; D3/D4 additionally depend on Track C (C1) and the
  standalone [ModCollector tool](plans/mod-collector.md).
- Track E depends only on Track P; it finishes an existing stub and exercises BFME2's existing
  ship content, so it needs neither Track C nor ModCollector. B4a (larger maps) should coordinate
  terrain streaming with E1's water-depth data once both land.

---

## Progress

Counts are against the [implementation checklist](implementation-checklist.md). Update when items are checked off.

### Track P — Playable Baseline

| Phase | Progress | Items |
|---|---|---|
| P0 — ReplaySketch harness | `███░░░░░░░` | 4 / 14 |
| P1 — Skirmish setup | `██████░░░░` | 5 / 8 |
| P2 — Skirmish AI | `░░░░░░░░░░` | 0 / 7 |
| P3 — Victory & defeat | `░░░░░░░░░░` | 0 / 9 |
| P4 — Economy loop | `░░░░░░░░░░` | 0 / 5 |
| P5 — Construction & production | `░░░░░░░░░░` | 0 / 5 |
| P6 — Combat & command | `░░░░░░░░░░` | 0 / 5 |
| P6b — Unit-contract validation | `░░░░░░░░░░` | 0 / 8 |
| P7 — End-to-end validation | `░░░░░░░░░░` | 0 / 6 |
| Track P total | `█░░░░░░░░░` | 9 / 67 |

### Tracks A / B / C (gated on Track P)

| Track | Progress | Items |
|---|---|---|
| Track A — Foundations | `░░░░░░░░░░` | 0 / 18 |
| Track B — Scale | `░░░░░░░░░░` | 0 / 12 |
| Track C — External Assets | `░░░░░░░░░░` | 0 / 6 |

### Track D — Gameplay Content Expansion (gated on Track P; D3/D4 also on Track C/ModCollector)

| Phase | Progress | Items |
|---|---|---|
| D1 — Tech tree / prerequisite graph | `░░░░░░░░░░` | 0 / 1 |
| D2 — Era/age progression | `░░░░░░░░░░` | 0 / 1 |
| D3 — External content ingestion | `░░░░░░░░░░` | 0 / 1 |
| D4 — Reference non-SAGE scenario | `░░░░░░░░░░` | 0 / 1 |
| Track D total | `░░░░░░░░░░` | 0 / 4 |

### Track E — Naval Warfare (gated on Track P only)

| Phase | Progress | Items |
|---|---|---|
| E1 — Water/ground terrain passability | `░░░░░░░░░░` | 0 / 1 |
| E2 — Ship combat | `░░░░░░░░░░` | 0 / 1 |
| E3 — Naval production | `░░░░░░░░░░` | 0 / 1 |
| E4 — Reference naval scenario | `░░░░░░░░░░` | 0 / 1 |
| Track E total | `░░░░░░░░░░` | 0 / 4 |

---



> Iterating on the plan? Follow [`agent/MAINTAINING.md`](agent/MAINTAINING.md). It is the
> planning-agent protocol for propagating a change across the parallel roadmap docs. The bullets
> below are only a summary.

- Update a phase's Status in its track document when the phase changes state.
- Keep the [Progress](#progress) tables in sync with checklist done/total counts and bars.
- Keep the [implementation checklist](implementation-checklist.md) in sync as granular changes land,
  and add finer items as a phase gains detail.
- When a phase gets a detailed design, add a doc under [`deep-dives/`](deep-dives/) and link it
  from the phase row and the [Deep dives](#deep-dives) table.
- Refresh [`agent/current-state.md`](agent/current-state.md) when it drifts from the code; it
  underpins each phase's assumptions.
- Record significant decisions in [`open-questions.md`](open-questions.md) and update the affected
  phase.
- Keep this README's tables in sync when documents or deep dives are added or removed.
- Update the [visual roadmap tree](roadmap-tree.md) whenever a phase, sub-phase, or dependency is
  added, removed, or renamed.
- Update the [seam registry](seams.md) whenever a phase introduces a new pluggable seam or a new
  implementation behind an existing seam.
- When a phase is ready for cheap or automated agents, decompose it into task cards in
  [`agent/`](agent/) and keep card Status aligned with phase Status.
