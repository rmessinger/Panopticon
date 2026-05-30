# Track A — Foundations

> Part of the [Large-Scale RTS Roadmap](README.md). Shared prerequisites that both the
> [Scale](track-b-scale.md) and [External Assets](track-c-external-assets.md) tracks build on.
> See [`current-state.md`](current-state.md) for the architecture these phases modify.

| Phase | Goal | Status |
|---|---|---|
| **A1. Headless simulation host** | Run the logic core with no renderer/audio/window. Extract a `SimulationHost` that owns `GameLogic`/scene logic and ticks at a fixed rate, so batch and distributed scenarios become possible. Emits the run outputs that [A5](deep-dives/visualization.md) analyzes. **Detailed design:** [deep-dives/headless-host.md](deep-dives/headless-host.md). | Not started |
| **A2. Reproducibility & statistical stability** | Make runs *comparable*, not bit-exact (see the [relaxed-determinism Decision](open-questions.md)). Centralize all randomness through a seeded source so experiments are controllable and re-runnable; audit uncontrolled nondeterminism; define the **conflict-resolution rules** for concurrent writes; add a verification harness that checks metric/invariant stability within tolerance (and an opt-in strict-hash mode for debugging). | Not started |
| **A3. Simulation/presentation seam** | Formalize the boundary between logic state and presentation (`Drawable`/render/audio). Logic must not read render-only state. Enables headless + future thin clients. | Not started |
| **A4. Experiment framework seams & harness** | The backbone of the project's "pluggable experiments" aim: establish stable seams with swappable implementations for optimization-relevant subsystems (e.g. the **simulation domain model**, pathfinding, partitioning, fidelity policy, storage layout, model import), plus an **experiment harness** that selects/configures an implementation, runs it against [B1](track-b-scale.md) benchmarks, and reports results to the [visualizer (A5)](deep-dives/visualization.md). Makes techniques interchangeable and independently measurable. | Not started |
| **A5. Scenario authoring & visualization** | The human-facing layer that makes the headless host usable: **author scenarios** (declarative + visual + capture-from-run), drive **parameter sweeps/experiment matrices** (with [A4](deep-dives/visualization.md)), and **replay/visualize/compare** runs — including drilling into the tiered recording to **localize divergences**. **Detailed design:** [deep-dives/visualization.md](deep-dives/visualization.md). | Not started |

## Notes

- **A4** is the foundation that realizes the roadmap's North Star (a framework for plugging in
  and measuring simulation-optimization experiments). Most Track B phases (B3 storage, B4
  pathfinding/partitioning, B6 partition scheme, B7 fidelity policy) should be expressed as
  *implementations behind A4 seams* rather than one-off hard-wired changes.
- **A2** underpins both **B3** (parallel logic must stay *comparable* and obey the
  conflict-resolution rules) and **B6** (cross-shard state sync validated by invariants, not
  bit-equality). Per the [relaxed-determinism Decision](open-questions.md), A2 targets
  reproducibility-for-comparison, not bit-exactness.
- **A3** coordinates with **C1**: both touch the presentation seam, so the model-import
  abstraction should feed the same runtime `Model`/`ModelInstance` types the rendering refactor
  expects.
- **A4** depends on the [A1 host](deep-dives/headless-host.md) and
  [B1](track-b-scale.md) benchmarks existing as the run/measure surface, and on **A2**
  determinism so experiment comparisons are reproducible.
- **Simulation Domain Model (an A4 seam).** The simulation *domain* — what entities exist and how
  they interact and partition — is itself a pluggable seam, so the scale infrastructure (the
  [FTRT headless host (A1)](deep-dives/headless-host.md) and [distributed sim (B6)](track-b-scale.md))
  is **not hard-wired to RTS** and can host other models (city builders, transportation/logistics
  sims, agent-based sims). A domain model supplies:
  - **Entity/component schema** — the entity kinds and their state (RTS units/buildings; city
    zones/utilities; transport vehicles/passengers/links).
  - **Topology / space model** — the partitionable structure: a 2-D spatial grid (RTS), a
    **graph/network** (road/rail/utility), or zone adjacency (city builder). Drives B6 partitioning
    and A5-4 divergence bucketing.
  - **Interaction model & locality** — how entities affect one another and the range used to
    derive halos: spatial radius (RTS weapon/vision), graph hop-distance/edge coupling (transport),
    or zone adjacency/flow coupling (city utilities).
  - **Interaction-resolution authority** — the rule for who resolves a cross-boundary interaction
    (RTS: initiator's shard; transport: edge owner; flows: conservation at the cut).
  - **Domain metrics/probes** — the outputs A1 records and the visualizer charts (RTS:
    casualties/APM/economy; city: population/traffic/utility coverage; transport:
    throughput/delay/utilization).
  - **Fidelity hooks** — domain-defined LOD (aggregate crowds vs. aggregated traffic flows),
    consumed by [B7](deep-dives/variable-fidelity.md).
  RTS is simply the **first** domain implementation; the existing `GameObject`/module engine is
  the reference adapter.
- **A5** depends on [A1-5 run outputs](deep-dives/headless-host.md) (the artifacts it replays and
  compares) and on **A4** for the experiment matrix/sweep wiring; its visualizations are
  **domain-parameterized** (the A4 domain model supplies the view). It is the validation surface
  for [B3](track-b-scale.md) parallelism and [B6](deep-dives/distributed-simulation.md) sharding —
  comparing within tolerance and localizing drift, not asserting bit-equality.
