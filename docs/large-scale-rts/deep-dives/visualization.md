# Deep Dive: Scenario Authoring & Visualization

> Expands phase **A5** of [Track A — Foundations](../track-a-foundations.md). Part of the
> [Large-Scale RTS Roadmap](../README.md). First-pass design — refine as phases land.

**Goal:** the human/agent-facing surface of the experiment framework — **set up** a simulation
scenario going in, and **replay/analyze/compare** its results coming out. This is the bookend to
the [headless/FTRT host (A1)](headless-host.md): A1 *runs* experiments and emits recordings; A5
is how you *author* what it runs and *make sense of* what it produced.

Both ends are **domain-parameterized** via the
[Simulation Domain Model seam (A4)](../track-a-foundations.md): the same authoring and
visualization machinery serves RTS battles, city builders, and transportation/logistics sims, with
the domain supplying entity kinds, topology, and metrics. RTS is the reference domain.

---

## 1. Why a dedicated layer

A scaling/optimization framework is only usable if experiments are easy to *define* and results
are *inspectable*. Today both are missing:

- **Setup** is purely programmatic — `SimulationHost` input injection (A1-4), harness config
  (A4), benchmark scenario builders (B1), and map authoring in WorldBuilder (B4a). There is no
  way to lay out initial conditions, parameterize a sweep, or save/version a scenario as a
  first-class artifact.
- **Results** are opaque without the replayer/visualizer (previously sketched as A1-6/A1-7, now
  owned here).

Pulling both into one layer keeps a single, domain-parameterized rendering/UX stack shared
between "before" (preview a scenario) and "after" (replay a run).

---

## 2. What already exists (reuse, don't reinvent)

| Capability | Current code | Reuse for |
|---|---|---|
| Replay parsing + action monitoring | `OpenSage.Tools.ReplaySketch.*`, `ReplayActionMonitor` | Loading runs, event logs, jump-to-frame. |
| Headless frame capture | `FrameCaptureService` (`[CAPTURE]` stdout, PNG export) | Exportable visual artifacts for CI. |
| Headless run loop | `OpenSage.Tools.TestHarness` `RunHeadless` | Driving preview/replay re-simulation. |
| Map authoring | `OpenSage.Tools.WorldBuilder.*` | Terrain/topology authoring substrate for scenario setup. |
| Windowed 3-D rendering | engine renderer / TestHarness windowed path | Optional full 3-D playback. |

---

## 3. Scenario setup & authoring

### 3.1 What a scenario *is*
A scenario is a versioned, declarative artifact capturing everything needed to make a run
**reproducible-for-comparison** (not bit-exact — see the
[relaxed-determinism Decision](../open-questions.md)):

- **Domain selection** — which [domain model (A4)](../track-a-foundations.md) (RTS / city /
  transport / …) and its configuration.
- **World / topology** — the map (spatial grid, via [B4a](larger-maps.md)), graph/network, or
  zone layout the domain partitions over.
- **Initial entity population** — placements/seeds: explicit unit/building placement (RTS),
  zoning + initial buildings (city), the network + initial vehicles/demand (transport). Supports
  **procedural seeding** (e.g. "N units per side in formation") for large stress scenarios.
- **Parameters** — domain + experiment knobs (counts, economy rates, AI profiles, fidelity
  policy, partition scheme, pathfinder choice — i.e. the [A4 seam](../track-a-foundations.md)
  selections under test).
- **Seed** — the master RNG seed (A2) so the scenario is re-runnable.
- **Input/order timeline** — scripted orders/events injected over time via the host's
  `InjectOrders` hook (A1-4).
- **Stop conditions & probes** — end criteria (frame cap, victory, steady-state) and which
  [domain metrics](headless-host.md) to record.

### 3.2 Authoring surfaces
- **Declarative scenario file (primary).** A human- and agent-writable scenario definition that
  the harness and host consume directly — the source of truth, diff-able and version-controllable.
  Agent-authored scenarios (and the [`.exchange`](../../../.github/copilot-instructions.md) hand-off)
  are first-class.
- **Visual setup (secondary, GPU-light).** A domain-parameterized editor that reuses the §4
  rendering: place/paint entities on the spatial/graph/zone view, edit parameters, and scrub a
  preview. For spatial domains this extends **WorldBuilder**; for graph/zone domains it renders
  the network/district view. Visual edits serialize back to the declarative file.
- **Capture-from-run.** Derive a new scenario from any frame of a previous run's recorded state
  (a recorded snapshot *is* a valid initial population) — e.g. "fork the battle at frame 4000."

### 3.3 Parameter sweeps / experiment matrix
Optimization experiments are rarely a single run. A5 lets a scenario declare **sweep axes**
(e.g. unit count ∈ {1k, 10k, 100k} × partition scheme ∈ {grid, graph} × fidelity ∈ {off, on}); the
[A4 harness](../track-a-foundations.md) expands the matrix, dispatches one host run per cell
(parallelizable, and the cells can themselves be sharded via [B6](distributed-simulation.md)), and
collects results keyed by cell for side-by-side comparison in §4/§5.

### 3.4 Pre-run validation & preview
Before committing to a long FTRT run, sanity-check the scenario: schema/seed validity, entity
placements inside the world, topology connectivity, probe wiring — and a short **preview** (render
frame 0 + step a few frames) so authoring mistakes are caught in seconds, not after an hour-long
batch.

---

## 4. Results replay & visualization

A tool to **replay recorded runs and analyze/visualize their results**, so headless/FTRT batch
runs are inspectable rather than opaque.

- **Load a run** from its recorded artifacts: the recorded state snapshots/deltas and the
  metrics/events/signature stream from [A1-5 run outputs](headless-host.md) (and/or the `.rep`
  inputs for re-running).
- **State reconstruction from recorded snapshots/deltas** — reconstruct any frame from what was
  recorded. Re-simulation through the `SimulationHost` is available for *approximate*
  reconstruction or re-running, but (per the [relaxed-determinism Decision](../open-questions.md))
  it yields a statistically-equivalent run, not a bit-identical one — so authoritative analysis
  uses the recorded state.
- **Timeline scrubbing** — step/seek to any logic frame; play/pause/fast-forward.
- **Visualizations (domain-parameterized — the domain model supplies the view):**
  - A **spatial 2-D overview/heatmap** of entity positions/movement for grid domains (RTS), a
    **network/graph view** for transport domains (links, vehicles, congestion), or a **zone
    choropleth** for city builders (population/utility/traffic by district) — all GPU-light and
    suitable for CI artifacts.
  - Optional full **3-D playback** when a GPU is present (reuse the engine renderer / TestHarness
    windowed path).
  - **Time-series charts** of the domain's metrics (RTS unit/building counts, economy, casualties;
    city population/traffic; transport throughput/delay).
  - **Event log** with jump-to-frame (built on `ReplayActionMonitor`).
  - **Fidelity overlay** — colour entities by their [B7](variable-fidelity.md) fidelity level.
- **Headless/exportable mode** — render the 2-D overview and charts to image/HTML artifacts
  without a window, for CI and remote inspection (extends `FrameCaptureService`'s
  agent-inspection workflow).

---

## 5. Run comparison & divergence localization

Detecting *that* two runs diverged is cheap; **localizing** *where* (which frame, partition,
entity, and system) is what makes the framework useful for validating parallelism
([B3](track-b-scale.md)) and sharding ([B6](distributed-simulation.md)). The
[relaxed-determinism Decision](../open-questions.md) makes this harder than in a bit-exact engine:
the classic "re-run with tracing and binary-search the first divergent frame" trick is
**unreliable**, because a re-run is only statistically equivalent — it may not reproduce the same
divergence. Localization detail therefore often has to be captured **in the original run**.

### 5.1 Tiered recording (emitted by [A1-5](headless-host.md), consumed here)
The recording is explicitly **tiered** so the always-on cost stays proportional to the very large
scales this roadmap targets:

| Tier | What it records | Cost | Purpose |
|---|---|---|---|
| **T0 — Aggregate** | Per-frame metrics + invariants (counts, conservation checks). | Always on, tiny. | Detect that a run drifted / broke an invariant. |
| **T1 — Hierarchical signature** | A signature *tree* per frame following the **domain topology** (world → partition → entity-bucket: spatial cells for grids, graph nodes/edges for networks, districts for city builders), quantized to tolerance. | Always on, small (one hash per node). | **Localize** drift by drilling down the tree to the first subtree that exceeds tolerance. |
| **T2 — Triggered detail** | Per-entity, per-system state deltas for a bounded frame+region window, into a ring buffer. | On-demand, large; only around a tripped threshold or a user-marked window. | **Pinpoint** the exact entity/system and inspect the values that diverged. |

### 5.2 Design points
- **Hierarchical, addressable signatures (T1).** A single global hash only answers "diverged
  yes/no at frame N." A tree keyed by partition and entity-bucket lets the comparator descend to
  the smallest node that diverged — turning O(world) inspection into O(log) drill-down.
- **Live triggering (T2), not re-run.** Because re-runs don't reproduce divergence, T2 is armed
  *before* a run and fires when a T1 subtree (or a T0 invariant) breaches tolerance, snapshotting
  recent detailed deltas from a ring buffer. A user can also pre-mark a frame/region window.
- **Tolerance-aware comparison.** "Divergence" is *first place drift exceeds a configured
  tolerance*, reported per subtree — not first non-identical bit. Tolerances are per-quantity
  (e.g. position ε, health ε) and surfaced in the comparator.
- **Stable bucketing across runs.** For two runs to be comparable, partition/entity buckets must
  be addressed by **stable keys** drawn from the domain topology (spatial cell, graph node/edge,
  or district id + stable entity id), not by iteration order or memory layout, so the same bucket
  lines up across runs that parallelized or partitioned differently.
- **Strict-mode interplay.** When the opt-in strict-hash mode is on (small scenarios), T1 nodes
  carry exact hashes and divergence localizes to the exact first frame/entity — best-of-both for
  debugging, at small scale.
- **Budgeted overhead.** T1 granularity (bucket size) and T2 ring-buffer depth are configurable so
  recording cost is bounded; at extreme scale T1 can coarsen and T2 can be armed only for suspect
  regions.

This makes the recorded artifacts granular enough to **identify where divergences occur** without
paying full per-entity-per-frame recording cost for every run.

---

## 6. Sub-phases

| Phase | Goal | Status |
|---|---|---|
| **A5-1. Scenario authoring & setup** | Define the declarative scenario artifact (§3.1) and the host/harness path that consumes it; procedural seeding for large stress scenarios; capture-from-run; pre-run validation + short preview (§3.4). Domain-parameterized. | Not started |
| **A5-2. Experiment matrix / parameter sweeps** | Let a scenario declare sweep axes; expand the matrix through the [A4 harness](../track-a-foundations.md), dispatch one run per cell, and key results for comparison (§3.3). | Not started |
| **A5-3. Results replayer / visualizer** | Build the analysis tool of §4 on top of ReplaySketch: load runs, scrub the timeline, domain-parameterized visualizations, metric charts, event log, fidelity overlay, headless export. *(Supersedes the former A1-6.)* | Not started |
| **A5-4. Divergence localization** | Implement the §5 drill-down: compare two runs' T1 signature trees to localize the first subtree exceeding tolerance, arm/inspect T2 detail, with stable cross-run bucketing and per-quantity tolerances. Validation surface for B3/B6. *(Supersedes the former A1-7.)* | Not started |

---

## 7. Sequencing notes

- **A5-1/3** can begin as soon as the [A1 host](headless-host.md) and [A1-5 run outputs](headless-host.md)
  exist — A5-1 needs the host's input/injection + initial-state path; A5-3 needs the recorded
  outputs.
- **A5-2 (sweeps)** depends on the [A4 harness](../track-a-foundations.md) (it expands and
  dispatches the matrix) and pairs naturally with [B1 benchmarks](track-b-scale.md).
- **A5-4 (divergence localization)** depends on A1-5's tiered recording (T1/T2) and the A5-3
  comparator; it is what lets [B3](track-b-scale.md) (parallelism) and
  [B6](distributed-simulation.md) (sharding) be debugged by pinpointing *where* a run drifts.
- A5 shares one **domain-parameterized rendering stack** between setup preview (§3.2/§3.4) and
  results replay (§4) — build it once.

## 8. Related

- Headless/FTRT host (produces the runs/recordings A5 consumes): [`headless-host.md`](headless-host.md)
- Experiment framework seams & harness (sweeps, domain model): [`../track-a-foundations.md`](../track-a-foundations.md)
- Distributed sim (validated via §5 localization): [`distributed-simulation.md`](distributed-simulation.md)
- Variable fidelity (fidelity overlay): [`variable-fidelity.md`](variable-fidelity.md)
- Larger maps (spatial authoring substrate): [`larger-maps.md`](larger-maps.md)
- Relaxed-determinism Decision: [`../open-questions.md`](../open-questions.md)
