# Roadmap Tree

> Part of the [Large-Scale Simulation Roadmap](README.md). This document is the visual overview of
> the roadmap: tracks → phases → sub-phases, with dependency flow. Keep it aligned with the track
> documents ([A](track-a-foundations.md) · [B](track-b-scale.md) ·
> [C](track-c-external-assets.md) · [D](track-d-gameplay-expansion.md) ·
> [E](track-e-naval-warfare.md) · [F](track-f-terrain-destruction.md) ·
> [G](track-g-rendering-clarity.md) · [H](track-h-storage-formats.md)) and the deep dives as phases,
> sub-phases, or dependencies change; see [Tree maintenance](#tree-maintenance).
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

Tracks → phases → sub-phases. Phases with a dedicated design document are marked ⮕.

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
      P6c Special-power implementation ⮕
        P6c-1 Cleanup area
        P6c-2 Object hack powers
        P6c-3 Hijacker hijack
        P6c-4 Validation-ground measurement
      P7 End-to-end skirmish validation ⮕
      P8 Implementation delta remediation ⮕
      P9 Unit validation ground ⮕
        P9-1 Primary-action model
        P9-2 Scenario and layout
        P9-3 Direct-spawn host
        P9-4 Action timer
        P9-5 TestHarness wiring
        P9-6 Report output
        P9-7 Baseline and regression gate
        P9-8 Test coverage
    A. Foundations
      A1 Headless host ⮕
        A1-1 Seam audit
        A1-2 Data-only asset loading
        A1-3 Logic-tick extraction
        A1-4 SimulationHost FTRT
        A1-5 Run outputs T0/T1/T2
      A2 Statistical validation
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
      C1 Import abstraction ⮕
      C2 Skeleton retargeting
      C3 Material mapping
      C4 First production format
      C4a EE2 NIF proof ⮕
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
    F. Object physics terrain and destruction
      F1 Deformation data model
      F2 Runtime mesh and rendering update
      F3 Weapon-driven terrain deformation
      F4 Physics engine integration ⮕
      F4a Game-object physics adoption ⮕
        PS-2 Participation lifecycle and contacts
        PS-3 Loose dynamics
        PS-4 Ground movers
        PS-5 Airborne objects
        PS-6 Static and kinematic objects
        PS-7 Projectile policy
        PS-8 Watercraft
        PS-9 Bepu default cutover
      F5 Physics-based structural destruction
      F6 Destruction-terrain interaction
      F7 Persistence and replay
      F8 Authoring and validation tooling
    G. Rendering clarity and image quality
      G1 High-DPI drawable and resolution correctness ⮕
        G1-1 Consumer classification audit
        G1-2 AllowHighDpi and drawable size
        G1-3 Render surfaces in pixels
        G1-4 2D and picking scale
      G2 Graphics settings and fidelity presets
      G3 Visual regression harness
      G4 Multisample antialiasing
      G5 Cutout and alpha-test clarity
      G6 Gamma-correct rendering
      G7 Texture filtering and mip quality
      G8 Shadow quality tier
      G9 Resolution-independent 2D and UI
      G10 Realistic proportions
    H. Map and unit storage formats
      H1 Format requirements and current-state audit
      H2 Format-agnostic interchange abstraction
      H3 New map storage format
      H4 New object and unit-definition storage format
      H5 Legacy migration and compatibility tooling
      H6 Authoring and pipeline integration
      H7 Validation and round-trip regression
```

---

## 2. Progression (dependency flow)

This left-to-right progression shows when phases become workable. Arrows mean "must precede."
Nodes keep their track colour (P = stone, A = blue, B = green, C = amber, D = pink, E = teal,
F = red, G = violet, H = olive). Track G is the one track not gated by Stage P: it changes
presentation only.
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
  classDef trackF fill:#fdeaea,stroke:#b5453a,color:#321;
  classDef trackG fill:#f3eefb,stroke:#7a5aa8,color:#221;
  classDef trackH fill:#f6f9e8,stroke:#7a8a2f,color:#122;
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
    P6C["P6c Special-power implementation"]
    P8["P8 Implementation delta remediation"]
    P7["P7 End-to-end skirmish validation"]
    P9["P9 Unit validation ground"]
  end

  subgraph S0["Stage 0 · Foundations & seams"]
    direction TB
    A2["A2 Statistical validation"]
    A4S["A4-1 Domain Model seam"]
    A3["A3 Sim/presentation seam"]
    A5WB["A5-6 WorldBuilder baseline"]
    B1["B1 Benchmarking utilities"]
    C1["C1 Import abstraction"]
    D1["D1 Tech tree prerequisite graph"]
    E1["E1 Water and ground terrain passability"]
    G1["G1 High-DPI & resolution correctness"]
    G2["G2 Graphics settings & presets"]
    H1["H1 Format requirements & audit"]
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
    E3["E3 Naval production"]
    G3["G3 Visual regression harness"]
    H2["H2 Format-agnostic interchange abstraction"]
  end

  subgraph S2["Stage 2 · Harness & core scale"]
    direction TB
    A4["A4 Experiment harness"]
    B3["B3 Parallel logic"]
    C4["C4 glTF · static + skinned"]
    C4A["C4a EE2 NIF static proof"]
    D3["D3 External content ingestion"]
    F1["F1 Terrain deformation data model"]
    F4["F4 Physics engine integration"]
    G4["G4 Multisample antialiasing"]
    G6["G6 Gamma-correct rendering"]
    G7["G7 Texture filtering & mip quality"]
    G8["G8 Shadow quality tier"]
    G9["G9 Resolution-independent 2D & UI"]
    G10["G10 Realistic proportions"]
    H3["H3 New map storage format"]
    H4["H4 New object/unit-definition storage format"]
  end

  subgraph S3["Stage 3 · Adoption, scenario & tooling"]
    direction TB
    A5["A5 Scenario & visualization"]
    C5["C5 Authoring tooling"]
    C6["C6 Extended features (deferred)"]
    D4["D4 Reference non-SAGE scenario"]
    F2["F2 Runtime mesh & rendering update"]
    F3["F3 Weapon-driven terrain deformation"]
    F4A["F4a Game-object physics adoption"]
    G5["G5 Cutout & alpha-test clarity"]
    H5["H5 Legacy migration & compatibility tooling"]
  end

  subgraph S4["Stage 4 · Fidelity scaling & physics consumers"]
    direction TB
    B6["B6 Distributed simulation"]
    B7["B7 Variable fidelity"]
    E2["E2 Ship combat"]
    F5["F5 Physics-based structural destruction"]
    H6["H6 Authoring & pipeline integration"]
  end

  subgraph S5["Stage 5 · Integration & round-trip validation"]
    direction TB
    E4["E4 Reference naval scenario"]
    F6["F6 Destruction-terrain interaction"]
    F7["F7 Persistence & replay"]
    H7["H7 Validation & round-trip regression"]
  end

  subgraph S6["Stage 6 · Terrain/destruction tooling"]
    direction TB
    F8["F8 Authoring & validation tooling"]
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
  P3 --> P8
  P4 --> P8
  P5 --> P8
  P6 --> P8
  P8 --> P7
  P0 --> P9
  P6 --> P9
  P6B --> P6C
  P6C --> P9
  P7 --> A2
  P7 --> A3
  P7 --> A4S
  P7 --> A5WB
  P7 --> B1
  P7 --> C1
  P7 --> D1
  P7 --> E1
  P7 --> H1

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
  E1 --> E3
  H1 --> H2

  %% Stage 1 -> 2
  A1 --> A4
  A2 --> A4
  B1 --> A4
  A2 --> B3
  B2 --> B3
  C1 --> C4
  C1 --> C4A
  C2 --> C4
  C3 --> C4
  D1 --> D3
  C1 --> D3
  B4A --> F1
  A1 --> F4
  B1 --> F4
  G1 --> G3
  G2 --> G3
  H2 --> H3
  H2 --> H4

  %% Stage 2 -> 3
  A1 --> A5
  A4 --> A5
  C4 --> C5
  C4 --> C6
  D2 --> D4
  D3 --> D4
  F1 --> F2
  B5 --> F2
  F1 --> F3
  F4 --> F4A
  P6B --> F4A
  G2 --> G4
  G3 --> G4
  G2 --> G10
  G3 --> G10
  G2 --> G6
  G3 --> G6
  G2 --> G7
  G3 --> G7
  G2 --> G8
  G3 --> G8
  G1 --> G9
  G3 --> G9
  H3 --> H5
  H4 --> H5

  %% Stage 3 -> 4
  A4S --> B6
  B4A --> B6
  A1 --> B6
  A2 --> B6
  B3 --> B6
  F4A --> B6
  B1 --> B7
  A2 --> B7
  B3 --> B7
  A5 --> B7
  F4A --> B7
  E1 --> E2
  F4A --> E2
  F4A --> F5
  H5 --> H6

  %% Stage 4 -> 5
  E2 --> E4
  E3 --> E4
  F1 --> F6
  F3 --> F6
  F4A --> F6
  F5 --> F6
  F1 --> F7
  F3 --> F7
  F4A --> F7
  F5 --> F7
  G4 --> G5
  H5 --> H7
  H6 --> H7

  %% Stage 5 -> 6
  F2 --> F8
  F6 --> F8
  F7 --> F8

  class A1,A2,A3,A4,A5,A5WB trackA;
  class B1,B2,B3,B4,B4A,B5,B6,B7 trackB;
  class C1,C2,C3,C4,C4A,C5,C6 trackC;
  class F1,F2,F3,F4,F4A,F5,F6,F7,F8 trackF;
  class G1,G2,G3,G4,G5,G6,G7,G8,G9,G10 trackG;
  class H1,H2,H3,H4,H5,H6,H7 trackH;
  class P0,P1,P2,P3,P4,P5,P6,P6B,P7,P8,P9 trackP;
  class D1,D2,D3,D4 trackD;
  class E1,E2,E3,E4 trackE;
  class A4S seam;
```

> Coordination, not ordering: A3 and C1 both touch the presentation seam, and B5 instancing
> should account for C4 (Track C) models. Those are coordination points, not strict dependencies,
> so they are omitted from the arrows above; see the [cross-track dependencies](README.md#cross-track-dependencies)
> note. D3 also depends on the standalone [ModCollector tool](plans/mod-collector.md), which is not
> part of a lettered track and has no engine dependency, so it is omitted as a node. H3 (new map
> storage format) coordinates with B4a-5 (chunked terrain storage) on the map-format terrain
> section; this is likewise a coordination point rather than a strict dependency, so no arrow
> connects them.

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
