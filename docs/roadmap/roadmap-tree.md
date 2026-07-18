# Roadmap Tree

> Part of the [Large-Scale Simulation Roadmap](README.md). This document is the visual overview of
> the roadmap: tracks → phases → sub-phases, with dependency flow. Keep it aligned with the track
> documents ([A](track-a-foundations.md) · [B](track-b-scale.md) ·
> [C](track-c-external-assets.md) · [D](track-d-gameplay-expansion.md) ·
> [E](track-e-naval-warfare.md)) and the deep dives as phases, sub-phases, or dependencies change;
> see [How to maintain](#how-to-maintain-this-tree).
>
> Audience: human overview of roadmap structure and dependency flow.

The diagrams below cover two views:

- [1. Structure tree](#1-structure-tree): hierarchy only, every track, phase, and sub-phase.
- [2. Progression](#2-progression-dependency-flow): phase-level dependency flow, including
  cross-track edges.

Status is not encoded here. The authoritative status for each phase lives in its track document.

> For interactive zoom and pan, open [`roadmap-tree.html`](roadmap-tree.html) over HTTP, for
> example with `python3 -m http.server` from this folder; browsers block local `file://` fetches.
> It reads the diagrams live from this file. GitHub's rendered Mermaid diagrams also expose built-in
> zoom and pan controls.

---

## 1. Structure tree

Tracks → phases → sub-phases. Phases with a dedicated deep dive are marked ⮕.

```mermaid
mindmap
  root((OpenSAGE<br/>simulation<br/>roadmap))
    P. Playable Baseline
      P0 ReplaySketch command harness ⮕
        P0-1 Open command model
        P0-1a MAUI screenshot mode
        P0-1b ViewModel testability
        P0-1c MAUI command-flow testability
        P0-2 Generic definition resolution
        P0-3 Exporter generalization
        P0-4 Assertion layer
        P0-5 Run-and-assert entry point
        P0-6 Large/procedural authoring
        P0-7 Agent validation playbook
      P1 Skirmish setup and relationships ⮕
      P2 Skirmish AI opponent ⮕
      P3 Victory and defeat ⮕
        P3-1 GameOutcome scaffold
        P3-2 Defeat and win rules
        P3-3 Death-defeat event
        P3-4 End-of-match flow
        P3-5 Mutual elimination sudden death
        P3-6 Time-limit score decision
      P4 Economy loop ⮕
      P5 Construction and production ⮕
      P6 Combat and command ⮕
      P6b Unit-contract validation ⮕
        P6b-1 Infantry archetype
        P6b-2 Vehicle archetype
        P6b-3 Aircraft archetype
        P6b-4 Builder archetype
        P6b-5 Support archetype
        P6b-6 Superweapon archetype
      P7 End-to-end skirmish validation
    A. Foundations
      A1 Headless host ⮕
        A1-1 Seam audit
        A1-2 Data-only asset loading
        A1-3 Logic-tick extraction
        A1-4 SimulationHost FTRT
        A1-5 Run outputs T0/T1/T2
      A2 Reproducibility and stability
      A3 Sim/presentation seam
      A4 Experiment framework ⮕
        A4-1 Simulation Domain Model seam
        A4-2 AI decision policy seam
        A4-3 Decentralized hierarchical AI seam ⮕
      A5 Scenario and visualization ⮕
        A5-1 Scenario authoring
        A5-2 Experiment matrix
        A5-3 Results visualizer
        A5-4 Divergence localization
        A5-5 Authoring and visualization at scale
        A5-6 WorldBuilder baseline
    B. Scale
      B1 Profiling and benchmarking utilities
      B2 Allocation cleanup
      B3 Parallel data-oriented logic
      B4 Spatial and pathfinding
      B4a Larger maps ⮕
        B4a-1 Baseline audit
        B4a-2 Region editing
        B4a-3 Large-map wizard
        B4a-4 Terrain streaming and LOD
        B4a-5 Chunked storage
        B4a-6 Round-trip and play
        B4a-7 Large-population scenario authoring
      B5 Rendering at scale
      B6 Distributed simulation ⮕
        B6-1 Partition and boundary spec
        B6-2 Single-process multi-shard
        B6-3 Handoff and halo correctness
        B6-4 Cross-shard interactions
        B6-5 Cross-shard routing/flow
        B6-6 Multi-process transport
        B6-7 Load balancing
      B7 Variable fidelity ⮕
        B7-1 Fidelity model and policy
        B7-2 Temporal LOD
        B7-3 Simplified behavior
        B7-4 Group aggregation
        B7-5 Precision reduction
        B7-6 Fidelity visualization
    C. External models and animations
      C1 Import abstraction
      C2 Skeleton retargeting
      C3 Material mapping
      C4 First concrete format
      C5 Authoring tooling
      C6 Extended features deferred
    D. Gameplay content expansion
      D1 Tech tree prerequisite graph ⮕
      D2 Era and age progression ⮕
      D3 External content ingestion
      D4 Reference non-SAGE scenario
    E. Naval warfare
      E1 Water and ground terrain passability ⮕
      E2 Ship combat ⮕
      E3 Naval production
      E4 Reference naval scenario
```

---

## 2. Progression (dependency flow)

This left-to-right progression shows when phases become workable. Arrows mean "must precede."
Nodes keep their track colour (P = stone, A = blue, B = green, C = amber, D = pink, E = teal).
The [playable-baseline prerequisite (Stage P)](track-p-playable-baseline.md) gates the whole
roadmap. The [Simulation Domain Model seam (A4-1)](track-a-foundations.md) is its own highlighted
node because it is an early prerequisite of A1 even though the rest of A4 follows A1.

```mermaid
flowchart LR
  classDef trackA fill:#eef2ff,stroke:#5566aa,color:#223;
  classDef trackB fill:#eafaf0,stroke:#3a8a5a,color:#132;
  classDef trackC fill:#fff6e9,stroke:#b5853a,color:#321;
  classDef trackD fill:#fdeef2,stroke:#a8477a,color:#310;
  classDef trackE fill:#e9f7fb,stroke:#2f8aa3,color:#032;
  classDef seam fill:#ffd9f2,stroke:#b3559a,stroke-width:2px,color:#312;
  classDef trackP fill:#f0ece4,stroke:#7a6f5a,color:#221;

  subgraph SP["Stage P · Playable baseline (prerequisite)"]
    direction TB
    P0["P0 ReplaySketch command harness"]
    P1["P1 Skirmish setup & relationships"]
    P4["P4 Economy loop"]
    P5["P5 Construction & production"]
    P2["P2 Skirmish AI opponent"]
    P3["P3 Victory & defeat"]
    P6["P6 Combat & command"]
    P6B["P6b Unit-contract validation"]
    P7["P7 End-to-end skirmish validation"]
  end

  subgraph S0["Stage 0 · Foundations & seams"]
    direction TB
    A2["A2 Reproducibility & stability"]
    A4S["A4-1 Domain Model seam"]
    A3["A3 Sim/presentation seam"]
    A5WB["A5-6 WorldBuilder baseline"]
    B1["B1 Benchmarking utilities"]
    C1["C1 Import abstraction"]
    D1["D1 Tech tree prerequisite graph"]
    E1["E1 Water and ground terrain passability"]
  end

  subgraph S1["Stage 1 · Core host & cleanup"]
    direction TB
    A1["A1 Headless host"]
    B2["B2 Allocation cleanup"]
    B4["B4 Spatial & pathfinding"]
    B4A["B4a Larger maps"]
    B5["B5 Rendering at scale"]
    C2["C2 Skeleton retargeting"]
    C3["C3 Material mapping"]
    D2["D2 Era and age progression"]
    E2["E2 Ship combat"]
    E3["E3 Naval production"]
  end

  subgraph S2["Stage 2 · Harness & core scale"]
    direction TB
    A4["A4 Experiment harness"]
    B3["B3 Parallel logic"]
    C4["C4 First format · static + skinned"]
    D3["D3 External content ingestion"]
    E4["E4 Reference naval scenario"]
  end

  subgraph S3["Stage 3 · Scenario, distribution & tooling"]
    direction TB
    A5["A5 Scenario & visualization"]
    B6["B6 Distributed simulation"]
    C5["C5 Authoring tooling"]
    C6["C6 Extended features (deferred)"]
    D4["D4 Reference non-SAGE scenario"]
  end

  subgraph S4["Stage 4 · Fidelity scaling"]
    direction TB
    B7["B7 Variable fidelity"]
  end

  %% Stage P (internal) -> Stage 0 gating
  P1 --> P4
  P1 --> P3
  P4 --> P5
  P1 --> P2
  P4 --> P2
  P5 --> P2
  P1 --> P6
  P6 --> P6B
  P0 --> P6B
  P4 --> P6B
  P5 --> P6B
  P2 --> P7
  P3 --> P7
  P4 --> P7
  P5 --> P7
  P6B --> P7
  P0 --> P7
  P7 --> A2
  P7 --> A3
  P7 --> A4S
  P7 --> A5WB
  P7 --> B1
  P7 --> C1
  P7 --> D1
  P7 --> E1

  %% Stage 0 -> 1
  A4S --> A1
  A2 --> A1
  B1 --> B2
  B1 --> B4
  B1 --> B4A
  A5WB --> B4A
  A3 --> B5
  C1 --> C2
  C1 --> C3
  D1 --> D2
  E1 --> E2
  E1 --> E3

  %% Stage 1 -> 2
  A1 --> A4
  A2 --> A4
  B1 --> A4
  A2 --> B3
  B2 --> B3
  C1 --> C4
  C2 --> C4
  C3 --> C4
  D1 --> D3
  C1 --> D3
  E2 --> E4
  E3 --> E4

  %% Stage 2 -> 3
  A1 --> A5
  A4 --> A5
  A4S --> B6
  B4A --> B6
  A1 --> B6
  A2 --> B6
  B3 --> B6
  C4 --> C5
  C4 --> C6
  D2 --> D4
  D3 --> D4

  %% Stage 3 -> 4
  B1 --> B7
  A2 --> B7
  B3 --> B7
  A5 --> B7

  class A1,A2,A3,A4,A5,A5WB trackA;
  class B1,B2,B3,B4,B4A,B5,B6,B7 trackB;
  class C1,C2,C3,C4,C5,C6 trackC;
  class P0,P1,P2,P3,P4,P5,P6,P6B,P7 trackP;
  class D1,D2,D3,D4 trackD;
  class E1,E2,E3,E4 trackE;
  class A4S seam;
```

> Coordination, not ordering: A3 and C1 both touch the presentation seam, and B5 instancing
> should account for C4 (Track C) models. Those are coordination points, not strict dependencies,
> so they are omitted from the arrows above; see the [cross-track dependencies](README.md#cross-track-dependencies)
> note. D3 also depends on the standalone [ModCollector tool](plans/mod-collector.md), which is not
> part of a lettered track and has no engine dependency, so it is omitted as a node.

---

## Tree maintenance

Update this file in the same change that alters roadmap structure:

- Add, remove, or rename a phase or sub-phase → update the matching node in the
  [structure tree](#1-structure-tree), and in the [progression](#2-progression-dependency-flow)
  if it is a phase. Keep node text terse; detail lives in the track docs and deep dives.
- Add or remove a dependency → add or remove the matching arrow in the
  [progression](#2-progression-dependency-flow). Keep these arrows consistent with `todo_deps`
  and the deep-dive sequencing notes.
- A phase gains a deep dive → add the ⮕ marker to its node.
- Placing a node in a stage → put the phase just after its latest hard prerequisite
  (stage = longest dependency chain from Stage 0). If a new dependency moves it later, shift it
  right so arrows still flow left-to-right.
- Do not encode per-phase status here. Update the Status column in the track document instead.

This document is linked from the [README document map](README.md#document-map).
