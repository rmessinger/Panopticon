# Track A — Foundations

> Part of the [Large-Scale RTS Roadmap](README.md). Shared prerequisites for
> [Scale](track-b-scale.md) and [External Assets](track-c-external-assets.md).
> See [`agent/current-state.md`](agent/current-state.md) for the architecture these phases modify.
>
> Audience: human — strategic track overview.

> The Milestone column states the demonstrable outcome that marks each phase done. Granular steps live in
> [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status |
|---|---|---|---|
| A1. Headless simulation host | Run the logic core with no renderer/audio/window. Extract a `SimulationHost` that owns `GameLogic`/scene logic and ticks at a fixed rate so batch and distributed scenarios become possible. Emits the run outputs [A5](deep-dives/visualization.md) analyzes. See [deep-dives/headless-host.md](deep-dives/headless-host.md). | Headless run reaches match end with no renderer/audio/window and writes the T0/T1/T2 run outputs. | Not started |
| A2. Reproducibility & statistical stability | Make runs comparable, not bit-exact (see the [relaxed-determinism Decision](open-questions.md)). Centralize all randomness through a seeded source so experiments are controllable and re-runnable; audit uncontrolled nondeterminism; define the conflict-resolution rules for concurrent writes; add a verification harness that checks metric/invariant stability within tolerance (and an opt-in strict-hash mode for debugging). | Repeated seeded runs of one scenario stay within tolerance, with an opt-in strict-hash mode that approaches bit-exactness for same-platform/same-build runs (best-effort — parallelism may still diverge; see the [A2 strict-hash scope Decision](open-questions.md)). | Not started |
| A3. Simulation/presentation seam | Formalize the boundary between logic state and presentation (`Drawable`/render/audio). Logic must not read render-only state. Enables headless + future thin clients. | A full logic tick executes with zero reads of render/audio-only state. | Not started |
| A4. Experiment framework seams & harness | Establish stable seams with swappable implementations for optimization-relevant subsystems, including the simulation domain model, pathfinding, partitioning, fidelity policy, storage layout, and model import. Add an experiment harness that selects and configures an implementation, runs it against [B1](track-b-scale.md) benchmarks, and reports results to the [A5 visualizer](deep-dives/visualization.md). This is the backbone of the project's pluggable-experiments aim: techniques become interchangeable and independently measurable. | Two implementations of one seam are config-selected, benchmarked on B1, and compared with no caller changes. | Not started |
| A5. Scenario authoring & visualization | Make the headless host usable: author scenarios (declarative + visual + capture-from-run), drive parameter sweeps and experiment matrices with [A4](deep-dives/visualization.md), and replay, visualize, and compare runs, including drilling into the tiered recording to localize divergences. ReplaySketch must scale with the sim through large/procedural authoring and streaming/LOD replay (A5-5). Also includes A5-6: WorldBuilder baseline, which audits functional state and verifies the basic author → save → load → play round-trip at normal map sizes. A5-6 can start early, does not depend on A1/A4, and gates B4a. See [deep-dives/visualization.md](deep-dives/visualization.md). | A scenario is authored, swept across a parameter matrix, and its runs replayed/compared with a divergence localized in the visualizer; and a normal-sized map round-trips through WorldBuilder and plays. | Not started |

## Phase resources

Navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams marked `†` are placeholder stubs; replace them before the first card for that phase is marked `Ready`.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| A1 | [Headless host](deep-dives/headless-host.md) | [§7](deep-dives/headless-host.md#7-architecture-diagrams) `†` | — |
| A2 | — | — | — |
| A3 | — | — | — |
| A4 | [Pluggable AI policy](deep-dives/pluggable-ai.md) | [§8](deep-dives/pluggable-ai.md#8-architecture-diagrams) | [`WI-A4-01`](agent/cards/WI-A4-01-extract-ai-decision-policy-interface.md), [`WI-A4-02`](agent/cards/WI-A4-02-ai-policy-selection-factory.md), [`WI-A4-03`](agent/cards/WI-A4-03-design-neural-network-policy.md), [`WI-A4-04`](agent/cards/WI-A4-04-implement-neural-network-policy.md) |
| A5 | [Visualization](deep-dives/visualization.md) | [§9](deep-dives/visualization.md#9-architecture-diagrams) `†` | — |

## Notes

- A4 realizes the roadmap's North Star: a framework for plugging in and measuring simulation-optimization experiments. Most Track B phases, including B3 storage, B4 pathfinding/partitioning, B6 partition scheme, and B7 fidelity policy, should land as implementations behind A4 seams rather than one-off hard-wired changes. See the [seam registry](seams.md) for the canonical list of seams, interfaces, and implementations.
- A2 underpins B3 and B6. Per the [relaxed-determinism Decision](open-questions.md), it targets reproducibility-for-comparison, not bit-exactness.
- A3 coordinates with C1 because both touch the presentation seam. The model-import abstraction should feed the same runtime `Model`/`ModelInstance` types the rendering refactor expects.
- A4 depends on the [A1 host](deep-dives/headless-host.md), [B1](track-b-scale.md) benchmarks as the run/measure surface, and A2 determinism for reproducible experiment comparisons.
- Simulation Domain Model is an A4 seam. The simulation domain, meaning what entities exist and how they interact and partition, is pluggable, so the scale infrastructure in the [A1 host](deep-dives/headless-host.md) and [B6 distributed sim](track-b-scale.md) is not hard-wired to RTS and can host other models such as city builders, transportation/logistics sims, and agent-based sims. A domain model supplies:
  - Entity/component schema — the entity kinds and their state (RTS units/buildings; city
    zones/utilities; transport vehicles/passengers/links).
  - Topology / space model — the partitionable structure: a 2-D spatial grid (RTS), a
    graph/network (road/rail/utility), or zone adjacency (city builder). Drives B6 partitioning
    and A5-4 divergence bucketing.
  - Interaction model & locality — how entities affect one another and the range used to
    derive halos: spatial radius (RTS weapon/vision), graph hop-distance/edge coupling (transport),
    or zone adjacency/flow coupling (city utilities).
  - Interaction-resolution authority — the rule for who resolves a cross-boundary interaction
    (RTS: initiator's shard; transport: edge owner; flows: conservation at the cut).
  - Domain metrics/probes — the outputs A1 records and the visualizer charts (RTS:
    casualties/APM/economy; city: population/traffic/utility coverage; transport:
    throughput/delay/utilization).
  - Fidelity hooks — domain-defined LOD (aggregate crowds vs. aggregated traffic flows),
    consumed by [B7](deep-dives/variable-fidelity.md).
  RTS is the first domain implementation; the existing `GameObject`/module engine is the reference adapter. Per the [domain-scope Decision](open-questions.md), non-RTS domains extend the `GameObject`/module system, meaning a domain is a set of modules plus a topology/interaction adapter that shares one entity runtime, save path, and B3 storage, rather than separate per-domain runtimes. A transport/graph domain is the first non-RTS validation target because it exercises graph-cut, non-spatial partitioning and edge-coupled interactions, proving the seam is topology-general rather than RTS-shaped. Per the [non-RTS domain completeness Decision](open-questions.md), that first transport/graph domain is only a minimal partition-stress harness: enough to exercise graph-cut partitioning, edge-coupled owner authority, and partition/boundary metrics, not a complete terminating Track-P-grade scenario.
- A5 depends on [A1-5 run outputs](deep-dives/headless-host.md), the artifacts it replays and compares, and on A4 for experiment matrix and sweep wiring. Its visualizations are domain-parameterized because the A4 domain model supplies the view. It is the validation surface for [B3](track-b-scale.md) parallelism and [B6](deep-dives/distributed-simulation.md) sharding: compare within tolerance and localize drift, not assert bit-equality.
- AI decision policy is an A4 seam. A player's strategic decisions, such as build, produce, and attack, are pluggable independently of actuation, which always stays `Order`s through `OrderProcessor` regardless of policy, matching [P2](track-p-playable-baseline.md)'s actuation decision. [Track P's P2](deep-dives/skirmish-ai.md) lands the reference rule-based state-machine implementation. The seam is what allows a second implementation, a small neural network whose weights are evolved through many seeded self-play matches scored from [`GameOutcome`](deep-dives/victory-defeat.md), to be config-selected and compared with no change to `AIPlayer`, `OrderProcessor`, or callers. See [deep-dives/pluggable-ai.md](deep-dives/pluggable-ai.md).
