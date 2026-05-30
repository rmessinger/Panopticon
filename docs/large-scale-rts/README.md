# Large-Scale RTS Simulation & External Asset Roadmap

> **Living roadmap.** Long-term plan for evolving OpenSAGE into an **extensible framework for
> simulation-optimization experiments** — a base on which to plug in and measure various ideas
> for scaling RTS simulation. It has **no fixed product goal**; the only fixed aims are
> **scalability** and **flexibility**. Specific capabilities (larger maps, distributed sim,
> variable fidelity, external assets, …) are *experiments plugged into the framework*, not
> destinations.
>
> This roadmap is split across several linked documents (see [Document map](#document-map)).
> Each phase has a **Status** (`Not started` / `In progress` / `Done` / `Revised`). When a
> phase grows its own detailed design, add a deep-dive under [`deep-dives/`](deep-dives/) and
> link it from the phase.

---

## Vision

The overarching aim is a **framework for plugging in and measuring simulation-optimization
experiments**. There is no fixed end-state — success is measured by how easily new scaling
ideas can be tried, swapped, and compared. Everything below is in service of two fixed aims:

- **Scalability** — push the simulation far beyond a single playable battle (more units,
  larger maps, headless/offline batch runs, distributed simulation, reduced per-unit cost).
- **Flexibility** — make techniques *pluggable and interchangeable* behind stable seams, so any
  given approach (a pathfinder, a fidelity policy, a partitioning scheme, a model format) can be
  replaced by an experiment without rewriting the engine.

Two concrete long-term thrusts give the framework something real to optimize and extend:

1. **Arbitrarily large simulations** — scale toward thousands–millions of entities, larger maps,
   headless/offline batch simulation, and distributed simulation across processes/machines.
2. **External asset incorporation** — let units use models and animations authored outside the
   Westwood `.w3d` ecosystem, via a format-agnostic import abstraction.

> These thrusts are *vehicles for experimentation*, not the point in themselves. New thrusts can
> be added as the framework matures. The **simulation domain itself is pluggable** (see the
> [Simulation Domain Model seam](track-a-foundations.md)): RTS is the first domain, but the
> scale infrastructure is designed to host other models too (city builders,
> transportation/logistics sims, agent-based sims).

### Guiding principles

- **Framework first: optimization techniques are pluggable experiments.** Prefer stable seams
  with swappable implementations (pathfinding, fidelity policy, partitioning, model import,
  storage layout) over hard-wiring any single approach. Each technique must be replaceable and
  independently measurable.
- **Measure everything.** Because the goal is experimentation, every technique is evaluated
  against benchmarks ([B1](track-b-scale.md)) and inspected with the
  [result replayer/visualizer](deep-dives/visualization.md). Cost/accuracy trade-offs are
  first-class outputs.
- **Domain-agnostic where it's cheap.** The simulation *domain* (entities, interactions,
  topology, metrics) is a pluggable seam, so the scale infrastructure — the
  [FTRT headless host](deep-dives/headless-host.md) and
  [distributed sim](deep-dives/distributed-simulation.md) — is not hard-wired to RTS and can host
  other simulation models. RTS is the reference domain, not a baked-in assumption.
- **Scale and extensibility win over original-engine fidelity when they conflict.** Reading
  original SAGE data remains *valuable* (and we should not break it gratuitously), but it is not
  allowed to block the architectural changes scale requires. Note the
  [relaxed-determinism Decision](open-questions.md) explicitly trades bit-exact, re-simulatable
  replays for parallel scale — recorded state, not input re-simulation, backs run analysis.
- **Separate "simulation" from "presentation" cleanly.** Headless and distributed scenarios
  require a logic core that runs with no renderer, audio, or window.
- **Reproducibility for comparison, not bit-exactness.** Per the
  [relaxed-determinism Decision](open-questions.md), the framework targets runs that are *stable
  enough to compare experiments* (seeded, re-runnable, metric/invariant-stable) rather than
  bit-identical. This buys aggressive intra-tick parallelism (with explicit conflict resolution)
  in exchange for giving up bit-exact replays.
- **Incremental, always-shippable.** Each phase should leave the engine building and the
  existing games playable.

---

## Document map

| Document | Contents |
|---|---|
| [`current-state.md`](current-state.md) | Snapshot of the architecture this roadmap builds on (cited). |
| [`track-a-foundations.md`](track-a-foundations.md) | Shared prerequisites: headless host, reproducibility & statistical stability, sim/presentation seam, experiment framework, scenario authoring & visualization. |
| [`track-b-scale.md`](track-b-scale.md) | Scaling work: benchmarks, allocation cleanup, parallelism, spatial/pathing, rendering, distributed sim. |
| [`track-c-external-assets.md`](track-c-external-assets.md) | External model/animation import: abstraction, retargeting, materials, first format, tooling. |
| [`open-questions.md`](open-questions.md) | Unresolved decisions; move answers inline and into the affected phase as they're made. |
| [`deep-dives/`](deep-dives/) | Per-phase detailed designs as they're written. |

### Deep dives

| Deep dive | Expands |
|---|---|
| [`deep-dives/headless-host.md`](deep-dives/headless-host.md) | Track A phase **A1 — Headless simulation host**. |
| [`deep-dives/visualization.md`](deep-dives/visualization.md) | Track A phase **A5 — Scenario authoring & visualization** (scenario setup, sweeps, result replayer/visualizer, divergence localization). |
| [`deep-dives/larger-maps.md`](deep-dives/larger-maps.md) | Track B phase **B4a — Larger maps (via WorldBuilder)**. |
| [`deep-dives/distributed-simulation.md`](deep-dives/distributed-simulation.md) | Track B phase **B6 — Distributed simulation** (spatial sharding). |
| [`deep-dives/variable-fidelity.md`](deep-dives/variable-fidelity.md) | Track B phase **B7 — Variable simulation fidelity** (scaling toolkit). |

---

## Tracks at a glance

The roadmap is organized into two parallel tracks (Scale, External Assets) plus a shared
Foundations track. Phases within a track are roughly ordered; cross-track dependencies are
noted in each track doc.

- **[Track A — Foundations](track-a-foundations.md):** A1 headless/FTRT host · A2 reproducibility &
  statistical stability · A3 simulation/presentation seam · A4 experiment framework seams & harness
  (incl. the **Simulation Domain Model** seam — RTS, city-builder, transport, …) · A5 scenario
  authoring & visualization.
- **[Track B — Scale](track-b-scale.md):** B1 benchmarks · B2 allocation cleanup · B3
  parallel/data-oriented logic · B4 spatial & pathfinding scaling · **B4a larger maps** · B5
  rendering at scale · B6 distributed simulation · **B7 variable simulation fidelity**.
- **[Track C — External Models & Animations](track-c-external-assets.md):** C1 import
  abstraction · C2 skeleton/animation retargeting · C3 material mapping · C4 first concrete
  format · C5 authoring/validation tooling.

### Cross-track dependencies

- **B5 (instanced rendering)** should account for **Track C** models so instancing is not
  W3D-specific.
- **C1** and **A3** both touch the presentation seam — coordinate so the import abstraction
  feeds the same runtime `Model`/`ModelInstance` types that the rendering refactor expects.

---

## How to maintain this roadmap

- Update a phase's **Status** when it changes state, in its track document.
- When a phase gets a detailed design, add a doc under [`deep-dives/`](deep-dives/) and link it
  from the phase row (and from the [Deep dives](#deep-dives) table above).
- When [`current-state.md`](current-state.md) drifts from the code, refresh it — it is the
  basis for every phase's assumptions.
- Record significant decisions in [`open-questions.md`](open-questions.md) (move an item from
  "open question" to a noted decision, and update the affected phase).
- Keep this README's tables in sync when documents or deep dives are added or removed.
