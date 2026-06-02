# Framework Seams

> Part of the [Large-Scale Simulation Roadmap](README.md). Defines what a seam is in this
> framework and maintains the authoritative registry of seams. The whole roadmap leans on
> seams (especially [A4](track-a-foundations.md)); this is the single place that names them, says
> what each one abstracts, and tracks their interface and implementations. Keep it in sync as
> seams are introduced or change — see [How to maintain](#how-to-maintain-this-registry).

## What is a seam?

A seam is a stable boundary that isolates one axis of variation so that alternative
implementations can be swapped behind it and measured independently, without changing the code
that uses it.

The term follows Michael Feathers' sense — "a place where you can alter behaviour without editing
in that place" — specialized here for the project's North Star (a framework for plugging in and
measuring simulation-optimization experiments). In this framework a seam has four properties:

1. One axis of variation. Each seam abstracts exactly one decision — how do we partition?,
   what is the simulation domain?, how do we store entity data? — not a grab-bag.
2. A stable interface. Callers depend on the interface, never on a concrete implementation.
3. Swappable, comparable implementations. Two or more implementations can sit behind it, and
   the [A4 experiment harness](track-a-foundations.md) can select one, run it against
   [B1 benchmarks](track-b-scale.md), and compare results.
4. A reference implementation. Usually the existing engine behaviour (e.g. RTS, W3D, the
   spatial grid) refactored to be one implementation — which proves the seam is real.

Two kinds of seam appear in the roadmap:

- Structural seams — architectural boundaries that decouple subsystems so the simulation can
  run in new contexts (e.g. headless, distributed). They are mostly enable/disable or
  present/absent, not a menu of techniques.
- Experiment seams — the [A4](track-a-foundations.md) swappable-implementation axes. These are
  where optimization techniques plug in as interchangeable, independently-measurable experiments.
  A Track B/C phase should generally be expressed as an implementation behind an experiment seam
  rather than a one-off hard-wired change.

> Rule of thumb: if a roadmap phase says "make X pluggable / swappable / domain-agnostic /
> behind an abstraction," it is introducing or widening a seam — add or update it here.

---

## Seam registry

Status legend: Reference = a working implementation exists (usually today's engine behaviour);
Planned = introduced by a roadmap phase; Future = anticipated implementations behind the
seam. Code references point at the current types the seam is (or will be) carved from.

### Structural seams

| Seam | Axis of variation | Interface (current → planned) | Implementations | Introduced / owned by | Docs |
|---|---|---|---|---|---|
| Graphics / GPU-optional | Whether logic needs a real GPU device | `GraphicsDevice` (Veldrid) → null/abstraction device behind the same surface | Reference: real Veldrid device · Planned: null device (headless) | [A1-1](deep-dives/headless-host.md) | [headless-host §3.1](deep-dives/headless-host.md) |
| Engine services | Logic-side services available to gameplay vs. presentation | `IGameEngine` / `GameEngine` (`src/OpenSage.Game/GameEngine.cs`) | Reference: full engine · Planned: logic-only engine for the `SimulationHost` | [A1-4](deep-dives/headless-host.md) | [headless-host §3](deep-dives/headless-host.md) |
| Simulation / presentation | Boundary between logic state and presentation (`Drawable`/render/audio) | New seam: logic must not read render-only state | Reference: today's coupled path · Planned: formalized boundary | [A3](track-a-foundations.md) | [track-a](track-a-foundations.md), [headless-host §3.1](deep-dives/headless-host.md) |
| Asset load strategy | Whether/when asset data is loaded vs. GPU-uploaded | `OnDemandAssetLoadStrategy` | Reference: on-demand + `None` · Planned: data-only logic load | [A1-2](deep-dives/headless-host.md) | [headless-host](deep-dives/headless-host.md) |

### Experiment seams (A4)

| Seam | Axis of variation | Interface (current → planned) | Implementations | Introduced / owned by | Docs |
|---|---|---|---|---|---|
| Simulation Domain Model | What the simulation is — entity/component schema, topology/space model, interaction model & locality, resolution authority, domain metrics, fidelity hooks | New seam (the central one) | Reference: RTS (`GameObject`/module engine) · First non-RTS validation: transport/graph · Future: city-builder, agent-based ([Decision](open-questions.md)) | [A4-1](track-a-foundations.md) | [track-a](track-a-foundations.md), [headless-host](deep-dives/headless-host.md), [distributed-sim](deep-dives/distributed-simulation.md) |
| Partition scheme | How the world is divided into shards/regions | `IPartitionScheme`-style abstraction (planned), built on `PartitionCellManager` / `Quadtree` (`src/OpenSage.Game/DataStructures/`) | Reference: spatial grid · Planned: graph-cut (transport) · Future: zone-adjacency (city) | [B6-1](deep-dives/distributed-simulation.md) | [distributed-sim](deep-dives/distributed-simulation.md) |
| Pathfinding / routing | How entities route across space/topology | Generalize `Navigation` A* (`src/OpenSage.Game/Navigation/`) | Reference: A* nav graph · Planned: hierarchical/flow-field; cross-shard routing; domain routing/flow | [B4](track-b-scale.md) / [B6-5](deep-dives/distributed-simulation.md) | [track-b](track-b-scale.md), [distributed-sim](deep-dives/distributed-simulation.md) |
| Entity storage layout | Memory/iteration layout of entity state | Behind the `GameObject`/module API (incremental data-oriented / SoA) | Reference: module objects · Planned: data-oriented hot-data storage | [B3](track-b-scale.md) | [track-b](track-b-scale.md), [open-questions (ECS lean)](open-questions.md) |
| Fidelity policy | How much detail an entity/region is simulated at (LOD) | Domain-supplied fidelity hooks + policy (planned) | Reference: full fidelity · Planned: temporal LOD, simplified behavior, aggregation, precision reduction | [B7-1](deep-dives/variable-fidelity.md) | [variable-fidelity](deep-dives/variable-fidelity.md) |
| Model / animation import | External asset format → engine interchange types | `IModelImporter` / `IAnimationImporter` (planned) behind `OnDemandAssetLoadStrategy` | Reference: W3D (refactored to one implementation) · Planned: glTF 2.0 (static + skinned) | [C1](track-c-external-assets.md) | [track-c](track-c-external-assets.md) |
| Randomness / conflict resolution | Source of randomness and how concurrent writes are reconciled | `IGame.CreateRandom()` seam + B3 intent/command buffers + merge/authority rules | Reference: per-context seeded RNG · Planned: intent-buffer merge + cross-shard authority rules | [A2](track-a-foundations.md) / [B3](track-b-scale.md) | [open-questions (relaxed determinism)](open-questions.md), [distributed-sim](deep-dives/distributed-simulation.md) |

> Derived, not separate: the [visualization](deep-dives/visualization.md) layer's views and the
> divergence-bucketing keys are parameterized by the Simulation Domain Model seam (the domain
> supplies the view and the stable bucket keys); they are not an independent seam.

---

## How to maintain this registry

- Adding a seam: when a phase introduces a new pluggable boundary, add a row to the correct
  table (structural vs. experiment) with its axis of variation, interface, implementations, and the
  owning phase. Cross-link the phase doc back here.
- Widening a seam: when a new implementation is planned or lands (e.g. a second partition
  scheme), update the Implementations cell and flip Reference/Planned/Future as appropriate.
- Naming: name a seam by its axis of variation (what varies), not by its first
  implementation — e.g. "Partition scheme," not "Spatial grid."
- Keep it singular: if a row is trying to abstract more than one axis, split it.
- This doc is linked from the [README document map](README.md#document-map); keep the
  [A4 notes](track-a-foundations.md) pointing here as the canonical seam list.
