# Roadmap Tree

> Part of the [Large-Scale Simulation Roadmap](README.md). This document is the visual overview of
> the roadmap: tracks → phases → sub-phases, with dependency flow. Keep it aligned with the track
> documents ([A](track-a-foundations.md) · [B](track-b-scale.md) ·
> [C](track-c-external-assets.md) · [D](track-d-gameplay-expansion.md) ·
> [E](track-e-naval-warfare.md) · [F](track-f-terrain-destruction.md) ·
> [G](track-g-rendering-clarity.md) · [H](track-h-storage-formats.md) ·
> [W](track-w-western-front.md) · [M](track-m-mass-battle.md) ·
> [N](track-n-pathfinding-performance.md) · [R](track-r-real-world-terrain.md) · [X](track-x-fixes-tweaks.md)) and the deep dives as phases,
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
  root((Panopticon<br/>simulation<br/>roadmap))
    X. Miscellaneous fixes and tweaks
      X1 Imported-unit hit feedback ⮕
        X1-1 Policy audit
        X1-2 Imported damage FX binding
        X1-3 Regression coverage
        X1-5 Directional impact and Rebel feedback design
        X1-6 Imported impact-effect selection
        X1-7 Attacker-facing impact placement
        X1-4 Readable duel capture
      X2 Capacity-probe graphics resource fixes
        X2-1 Upload lifetime cleanup
        X2-2 Combat FX and model reuse
      X3 Cursor modernization
        X3-1 Cursor theme evaluation and examples
      X4 ReplaySketch TabControl shell
        X4-1 TabControl tab strip
      X5 Render culling dispatch
        X5-1 Culling dispatch sizing
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
        P0-8 TestHarness launcher UI
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
        A1-6 Operational recording ⮕
      A2 Statistical validation
      A3 Sim/presentation seam
      A4 Experiment framework ⮕
        A4-1 Simulation Domain Model seam
        A4-2 AI decision policy seam
        A4-3 Decentralized hierarchical AI seam ⮕
        A4-4 Domain state projection ⮕
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
      B5 Rendering at scale ⮕
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
        B7-2 Temporal LOD (deferred)
        B7-3 Simplified behavior (deferred)
        B7-4 Group aggregation
        B7-5 Precision reduction (deferred)
        B7-6 Fidelity visualization
    C. External models and animations
      C1 Import abstraction ⮕
      C2 Skeleton retargeting ⮕
      C3 Material mapping ⮕
      C4 First production format ⮕
      C4a EE2 NIF proof ⮕
      C4b EE2 cooked textures ⮕
      C4c EE2 rigid KF animation ⮕
        C4c-1 KF block decoding
        C4c-2 Node hierarchy bones
        C4c-3 Key curve resampling
        C4c-4 KF animation importer
        C4c-5 Installed clip checks
        C4c-6 Animated captures
      C4d EE2 skinned animation ⮕
        C4d-1 NIF rotation convention
        C4d-2 Skin block decoding
        C4d-3 C1 four influences
        C4d-4 Runtime four influences
        C4d-5 glTF four influences
        C4d-6 NIF skin conversion
        C4d-7 Installed proof captures
      C4e EE2 property animation ⮕
        C4e-1 Controller block decoding
        C4e-2 Property target resolution
        C4e-3 C1 property channels
        C4e-4 NIF property conversion
        C4e-5 Runtime material animation
        C4e-6 Installed proof captures
      C4f EE2 scale animation ⮕
        C4f-1 C1 and runtime scale
        C4f-2 NIF rest scale
        C4f-3 KF scale conversion
        C4f-4 Scale normals and bounds
        C4f-5 EE2 scale descriptors
        C4f-6 Scale proof captures
      C4g EE2 morph animation ⮕
        C4g-1 Morph block decoding
        C4g-2 Morph interchange gate
        C4g-3 NIF morph target import
        C4g-4 CPU morph runtime
        C4g-5 Morph weight binding
        C4g-6 EE2 morph captures
      C4h EE2 particle effects ⮕
        C4h-1 Particle effect census
        C4h-2 Legacy particle block decoding
        C4h-3 Particle interchange
        C4h-4 Runtime template materialization
        C4h-5 Effect attachment binding
        C4h-6 Particle proof captures
      C4i EE2 legacy KF formats ⮕
        C4i-1 Legacy header layouts
        C4i-2 Legacy KF block reuse
        C4i-3 Legacy semantics normalization
        C4i-4 DDF proof binding
        C4i-5 Legacy rejection fixtures
        C4i-6 Legacy proof captures
      C5 Authoring tooling ⮕
      C6 Extended features deferred ⮕
      C7a SC2 CASC storage ⮕
        C7a-1 Build info and index
        C7a-2 BLTE and encoding
        C7a-3 MNDX root and MAR tries
        C7a-4 CASC file system
      C7b SC2 static M3 models ⮕
        C7b-1 M3 container reader
        C7b-2 M3 geometry conversion
        C7b-3 DDS decode and texture rules
        C7b-4 M3 material conversion
        C7b-5 Static proof captures
      C7c SC2 skinned M3 animation ⮕
        C7c-1 Per-axis scale and absolute clips
        C7c-2 M3 skin conversion
        C7c-3 M3 sequence conversion
        C7c-4 External m3a clips
        C7c-5 Animated proof captures
      C8a SAGE-family W3D corpus ⮕
      C8b BFME-family static meshes ⮕
      C8c BFME-family skinning and animation ⮕
      C9a TW3K packs ⮕
      C9b TW3K static RMV2 parts ⮕
      C9c TW3K skeletons and animation ⮕
      C9d TW3K composite soldiers ⮕
      C10a AoE3 DE archives and textures ⮕
      C10b AoE3 DE static Granny models ⮕
      C10c AoE3 DE skinned animation ⮕
      C10d Granny decompression ✓
      C11a Rise of Legends archives ⮕
      C11b Rise of Legends static BH3 models ⮕
      C11c Rise of Legends animation ⮕
      C12a SHOGUN 2 packs ⮕
      C12b SHOGUN 2 static models and parts ⮕
      C12c SHOGUN 2 animation and soldiers ⮕
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
        G1-3 Render surfaces and GUI in pixels
        G1-4 Mouse input to pixels
      G2 Graphics settings and fidelity presets ⮕
        G2-1 Render-state audit
        G2-2 Settings type and presets
        G2-3 Applied settings and resource lifecycle
      G3 Visual regression harness ⮕
        G3-1 Capture-path audit
        G3-2 Image-quality metrics
        G3-3 Fixed-scene capture pass
        G3-4 Golden store and regression gate
      G4 Multisample antialiasing ⮕
        G4-1 Sample-count inventory
        G4-2 Multisample targets and resolve
      G5 Cutout and alpha-test clarity ⮕
        G5-1 Material and mip audit
        G5-2 Coverage-preserving mips and alpha-to-coverage
      G6 Gamma-correct rendering ⮕
        G6-1 Colour-space audit
        G6-2 Linear shading and output dither
      G7 Texture filtering and mip quality ⮕
        G7-1 Sampler and mip audit
        G7-2 Anisotropy and mip bias
      G8 Shadow quality tier ⮕
        G8-1 Shadow-settings audit
        G8-2 Four-cascade tier and receiver slope bias
      G9 Resolution-independent 2D and UI ⮕
        G9-1 UI resolution audit
        G9-2 Native 2D and role-aware sampling
        G9-3 Dynamic HUD chrome
      G10 Realistic proportions ⮕
        G10-1 Reference-scale audit
        G10-2 Render-only scale overrides
        G10-3 Explicit opt-in and golden frames
      G11 Terrain impact decals ⮕
        G11-1 Decal store and scorch FX
        G11-2 Overlay mesh and pass
        G11-3 Logic and map producers
        G11-4 Enhanced lighting and goldens
    H. Map and unit storage formats
      H1 Format requirements and current-state audit
      H2 Format-agnostic map interchange abstraction
      H2a Unit-content import layer ⮕
      H2b Metric core units ⮕
      H3 New map storage format
      H4 New object and unit-definition storage format
      H5 Legacy migration and compatibility tooling
      H6 Authoring and pipeline integration
      H7 Validation and round-trip regression
    W. Western Front simulation
      W0 Charter fixture ⮕
      W1 Aggregate sector simulation ⮕
      W2 Fidelity promotion and reconciliation ⮕
      W3a Sedan 1870 ⮕
      W3b Somme sector ⮕
      W4 Command and logistics experiment ⮕
      W5 Distributed regional simulation ⮕
      W6 Long-run campaign experiment ⮕
    M. Mixed-roster mass battle
      M0 Battle map ⮕
      M1 Capacity probe ⮕
      M2 EE2 combat units ⮕
      M3 Mixed roster normalization ⮕
      M4 Army-scale AI ⮕
      M5 1000 vs 1000 battle ⮕
    N. Multi-unit pathfinding performance, deadlocks, and layered navigation
      N0 Path cost baseline ⮕
        N0-1 Path span and counters
        N0-2 Pathfinding load scenario
      N1 Search core efficiency ⮕
        N1-1 Allocation-free search
        N1-2 Region labels
        N1-3 Passability snapshot
      N2 Shared group requests ⮕
        N2-1 Follower landing
        N2-2 Group selection moves
      N3 Shared-destination fields ⮕
        N3-1 Goal field gate
      N4 Off-thread order paths ⮕
        N4-1 Pending path handles
        N4-2 Async order paths
      N5 Scale verification ⮕
        N5-1 Budget verification
      N6 Deadlock detection baseline ⮕
        N6-1 Movement progress and stall attribution
        N6-2 Navigation deadlock scenario
      N7 Path liveness ⮕
        N7-1 Async and squad path liveness
        N7-2 Offset route validation
      N8 Blocked-unit resolution ⮕
        N8-1 Wait and local repath
        N8-2 Give-way and mutual block
      N9 Destination crowding ⮕
        N9-1 Destination slots
        N9-2 Crowd arrival
      N10 Deadlock verification ⮕
        N10-1 Deadlock target verification
      N11 Passage detection and baseline ⮕
        N11-1 Passage finder
        N11-2 Passage cases and baseline
      N12 Passage queues ⮕
        N12-1 Coordinator, order, and lane path
        N12-2 Hold, release, and route splice
      N13 Opposing flow and group entry ⮕
        N13-1 Opposing flow arbitration
        N13-2 Squad and cluster entry
      N14 Passage verification ⮕
        N14-1 Passage target verification
      N15 Layered navigation contract and baseline ⮕
        N15-1 Navigation plan and layer contract
        N15-2 Layered cases and baseline
      N16 Strategic sector graph ⮕
        N16-1 Sector graph
        N16-2 Strategic planner
      N17 Corridor main path ⮕
        N17-1 Corridor search and stitching
        N17-2 Layered unit main path
      N18 Group local router ⮕
        N18-1 Navigation groups and spine
        N18-2 Width-adaptive formation
        N18-3 Crowd routing
      N19 Unit steering ⮕
        N19-1 Steering core
        N19-2 Steering integration
      N20 Layered navigation verification ⮕
        N20-1 Layered target verification
    R. Real-world terrain
      R1 Geospatial import core ⮕
        B4b-1 Input contract and dependency spike
        B4b-2 Geospatial interchange
        B4b-3 Local source readers
        B4b-4 Coordinate normalization
        B4b-5 Terrain and imagery generation
        B4b-6 Vector semantics
      R2 Standalone payload and dev preview ⮕
        B4b-6a Standalone payload
        B4b-6b Dev preview
      R3 Map package output ⮕
      R4 WorldBuilder import workflow ⮕
      R5 Validation and scale proof ⮕
```

---

## 2. Progression (dependency flow)

This left-to-right progression shows when phases become workable. Arrows mean "must precede."
Nodes keep their track colour (P = stone, A = blue, B = green, C = amber, D = pink, E = teal,
F = red, G = violet, H = olive, W = slate, M = rust, N = slate teal, R = moss). Track G is the one track not gated by Stage P: it changes
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
  classDef trackW fill:#eef0f3,stroke:#5a6a7a,color:#122;
  classDef trackM fill:#fbefe6,stroke:#a0583a,color:#311;
  classDef trackN fill:#eef7f7,stroke:#3a7a80,color:#123;
  classDef trackR fill:#f2f6ea,stroke:#6a8a4a,color:#121;

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
    R1["R1 Geospatial import core"]
  end

  subgraph S1["Stage 1 · Core host & cleanup"]
    direction TB
    A1["A1 Headless host"]
    X3["X3 Cursor modernization"]
    X4["X4 ReplaySketch TabControl shell"]
    B2["B2 Allocation cleanup"]
    B4["B4 Spatial & pathfinding"]
    B4A["B4a Larger maps"]
    B5["B5 Rendering at scale"]
    C2["C2 Skeleton retargeting"]
    C3["C3 Material mapping"]
    C7A["C7a SC2 CASC storage"]
    C8A["C8a SAGE-family W3D corpus"]
    C9A["C9a TW3K packs"]
    C10A["C10a AoE3 DE archives"]
    C11A["C11a Rise of Legends archives"]
    C12A["C12a SHOGUN 2 packs"]
    D2["D2 Era and age progression"]
    E3["E3 Naval production"]
    G3["G3 Visual regression harness"]
    H2["H2 Format-agnostic map interchange abstraction"]
    R2["R2 Standalone payload & dev preview"]
  end

  subgraph S2["Stage 2 · Harness & core scale"]
    direction TB
    A4["A4 Experiment harness"]
    A4DS["A4-4 Domain state projection"]
    B3["B3 Parallel logic"]
    C4["C4 glTF · static + skinned"]
    C4A["C4a EE2 NIF static proof"]
    C7B["C7b SC2 static M3 models"]
    C8B["C8b BFME-family static meshes"]
    C9B["C9b TW3K static RMV2 parts"]
    C10B["C10b AoE3 DE static Granny models"]
    C11B["C11b Rise of Legends static BH3 models"]
    C12B["C12b SHOGUN 2 static models and parts"]
    D3["D3 External content ingestion"]
    F1["F1 Terrain deformation data model"]
    F4["F4 Physics engine integration"]
    G4["G4 Multisample antialiasing"]
    G6["G6 Gamma-correct rendering"]
    G7["G7 Texture filtering & mip quality"]
    G8["G8 Shadow quality tier"]
    G9["G9 Resolution-independent 2D & UI"]
    G10["G10 Realistic proportions"]
    G11["G11 Terrain impact decals"]
    H3["H3 New map storage format"]
    M0["M0 Battle map"]
  end

  subgraph S3["Stage 3 · Adoption, scenario & tooling"]
    direction TB
    A5["A5 Scenario & visualization"]
    A1OR["A1-6 Operational recording"]
    W0["W0 Charter fixture"]
    C4B["C4b EE2 cooked textures"]
    C5["C5 Authoring tooling"]
    C6["C6 Extended features (deferred)"]
    C10D["C10d Granny decompression (done)"]
    C8C["C8c BFME-family skinning and animation"]
    D4["D4 Reference non-SAGE scenario"]
    F2["F2 Runtime mesh & rendering update"]
    F3["F3 Weapon-driven terrain deformation"]
    F4A["F4a Game-object physics adoption"]
    G5["G5 Cutout & alpha-test clarity"]
    M1["M1 Capacity probe"]
    R3["R3 Map package output"]
  end

  subgraph S4["Stage 4 · Fidelity scaling & physics consumers"]
    direction TB
    X2["X2 Capacity-probe graphics resource fixes"]
    X5["X5 Render culling dispatch"]
    B6["B6 Distributed simulation"]
    B7["B7 Variable fidelity"]
    C4C["C4c EE2 rigid KF animation"]
    E2["E2 Ship combat"]
    F5["F5 Physics-based structural destruction"]
    W1["W1 Aggregate sector simulation"]
    M4["M4 Army-scale AI"]
  end

  subgraph S5["Stage 5 · Integration & round-trip validation"]
    direction TB
    C4D["C4d EE2 skinned animation"]
    C4E["C4e EE2 property animation"]
    C4F["C4f EE2 scale animation"]
    C4G["C4g EE2 morph animation"]
    C4H["C4h EE2 particle effects"]
    C4I["C4i EE2 legacy KF formats"]
    E4["E4 Reference naval scenario"]
    F6["F6 Destruction-terrain interaction"]
    F7["F7 Persistence & replay"]
    W2["W2 Fidelity promotion & reconciliation"]
    N0["N0 Path cost baseline"]
  end

  subgraph S6["Stage 6 · Terrain/destruction tooling & theater experiments"]
    direction TB
    C7C["C7c SC2 skinned M3 animation"]
    C9C["C9c TW3K skeletons and animation"]
    C10C["C10c AoE3 DE skinned animation"]
    C11C["C11c Rise of Legends animation"]
    C12C["C12c SHOGUN 2 animation and soldiers"]
    F8["F8 Authoring & validation tooling"]
    W3A["W3a Sedan 1870"]
    W4["W4 Command & logistics experiment"]
    W5["W5 Distributed regional simulation"]
    H2A["H2a Unit-content import layer"]
    N1["N1 Search core efficiency"]
  end

  subgraph S7["Stage 7 · Somme sector & unit-content consumers"]
    direction TB
    C9D["C9d TW3K composite soldiers"]
    W3B["W3b Somme sector"]
    M2["M2 EE2 combat units"]
    X1["X1 Imported-unit hit feedback"]
    H4["H4 New object/unit-definition storage format"]
    H2B["H2b Metric core units"]
    N2["N2 Shared group requests"]
    N4["N4 Off-thread order paths"]
    N16["N16 Strategic sector graph"]
  end

  subgraph S8["Stage 8 · Long-run campaign & mixed rosters"]
    direction TB
    W6["W6 Long-run campaign experiment"]
    M3["M3 Mixed roster normalization"]
    H5["H5 Legacy migration & compatibility tooling"]
    N3["N3 Shared-destination fields"]
  end

  subgraph S9["Stage 9 · Unit-content authoring & mass battle"]
    direction TB
    H6["H6 Authoring & pipeline integration"]
    M5["M5 1000 vs 1000 battle"]
    N5["N5 Scale verification"]
  end

  subgraph S10["Stage 10 · Storage round-trip regression"]
    direction TB
    H7["H7 Validation & round-trip regression"]
    N6["N6 Deadlock detection baseline"]
    R4["R4 WorldBuilder import workflow"]
  end

  subgraph S11["Stage 11 · Navigation deadlock resolution"]
    direction TB
    N7["N7 Path liveness"]
    N8["N8 Blocked-unit resolution"]
    N9["N9 Destination crowding"]
    N11["N11 Passage detection and baseline"]
    R5["R5 Validation & scale proof"]
  end

  subgraph S12["Stage 12 · Navigation deadlock verification & passage queues"]
    direction TB
    N10["N10 Deadlock verification"]
    N12["N12 Passage queues"]
  end

  subgraph S13["Stage 13 · Passage flow & layered plans"]
    direction TB
    N13["N13 Opposing flow and group entry"]
    N15["N15 Layered navigation contract and baseline"]
  end

  subgraph S14["Stage 14 · Passage verification, corridor paths & steering"]
    direction TB
    N14["N14 Passage verification"]
    N17["N17 Corridor main path"]
    N19["N19 Unit steering"]
  end

  subgraph S15["Stage 15 · Group local routing"]
    direction TB
    N18["N18 Group local router"]
  end

  subgraph S16["Stage 16 · Layered navigation verification"]
    direction TB
    N20["N20 Layered navigation verification"]
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
  C1 --> C7A
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
  C7A --> C7B
  C1 --> C7B
  C3 --> C7B
  D1 --> D3
  C1 --> D3
  B4A --> F1
  A1 --> F4
  B1 --> F4
  G1 --> G3
  G2 --> G3
  H2 --> H3
  H2A --> H4
  H2A --> H2B
  A4S --> A4DS
  A1 --> A4DS

  %% Stage 2 -> 3
  A1 --> A5
  A4 --> A5
  C4 --> C5
  C4 --> C6
  C4A --> C4B
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
  G2 --> G11
  G3 --> G11
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
  A4S --> W0
  A4DS --> W0
  A1 --> A1OR
  A4DS --> A1OR

  %% Stage 3 -> 4
  A4S --> B6
  B4A --> B6
  C2 --> C4C
  C4B --> C4C
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
  W0 --> W1
  A2 --> W1

  %% Stage 4 -> 5
  C4C --> C4D
  C4C --> C4E
  C4C --> C4F
  C4C --> C4G
  C4C --> C4H
  C4C --> C4I
  C4B --> C4E
  C4B --> C4H
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
  R1 --> R2
  R2 --> R3
  B4A --> R3
  H3 --> R3
  R3 --> R4
  H6 --> R4
  R4 --> R5
  W1 --> W2
  A4 --> W2

  %% Stage 5 -> 6
  C7B --> C7C
  C2 --> C7C
  C4D --> C7C
  C4F --> C7C
  C1 --> C8A
  C8A --> C8B
  C8B --> C8C
  C2 --> C8C
  C1 --> C9A
  C9A --> C9B
  C3 --> C9B
  C9B --> C9C
  C2 --> C9C
  C4D --> C9C
  C1 --> C10A
  C10A --> C10B
  C3 --> C10B
  C10B --> C10C
  C2 --> C10C
  C4D --> C10C
  C1 --> C11A
  C11A --> C11B
  C3 --> C11B
  C11B --> C11C
  C2 --> C11C
  C4D --> C11C
  C1 --> C12A
  C12A --> C12B
  C3 --> C12B
  C12B --> C12C
  C2 --> C12C
  C4D --> C12C
  C9C --> C9D
  C10B --> C10D
  F2 --> F8
  F6 --> F8
  F7 --> F8
  W2 --> W3A
  R2 --> W3A
  A1OR --> W3A
  W2 --> W4
  A5 --> W4
  W2 --> W5
  B6 --> W5

  %% Stage 6 -> 7
  W3A --> W3B
  %% Stages 6-7 -> 8
  W3B --> W6
  W4 --> W6
  W5 --> W6
  P9 --> M0
  A1 --> M0
  M0 --> M1
  B1 --> M1
  M1 --> X2
  M1 --> X5
  C4D --> M2
  C4D --> H2A
  P9 --> H2A
  H2A --> M2
  H2A --> X1
  P9 --> X1
  P9 --> M2
  M2 --> M3
  M1 --> M4
  A4 --> M4
  M0 --> M5
  M3 --> M5
  M4 --> M5
  B4 --> M5
  B5 --> M5
  A5 --> M5
  B1 --> N0
  B4 --> N0
  M4 --> N0
  N0 --> N1
  N1 --> N2
  N1 --> N4
  N2 --> N3
  N2 --> N5
  N3 --> N5
  N4 --> N5
  N5 --> N6
  N6 --> N7
  N6 --> N8
  N6 --> N9
  N7 --> N10
  N8 --> N10
  N9 --> N10
  N6 --> N11
  N11 --> N12
  N12 --> N13
  N13 --> N14
  N10 --> N14
  N10 --> N15
  N1 --> N16
  N15 --> N17
  N16 --> N17
  N14 --> N18
  N17 --> N18
  N10 --> N19
  N15 --> N19
  N18 --> N20
  N19 --> N20

  class A1,A1OR,A2,A3,A4,A4DS,A5,A5WB trackA;
  class B1,B2,B3,B4,B4A,B5,B6,B7 trackB;
  class C1,C2,C3,C4,C4A,C4B,C4C,C4D,C4E,C4F,C4G,C4H,C4I,C5,C6,C7A,C7B,C7C,C8A,C8B,C8C,C9A,C9B,C9C,C9D,C10A,C10B,C10C,C10D,C11A,C11B,C11C,C12A,C12B,C12C trackC;
  class F1,F2,F3,F4,F4A,F5,F6,F7,F8 trackF;
  class G1,G2,G3,G4,G5,G6,G7,G8,G9,G10,G11 trackG;
  class H1,H2,H2A,H2B,H3,H4,H5,H6,H7 trackH;
  class P0,P1,P2,P3,P4,P5,P6,P6B,P7,P8,P9 trackP;
  class D1,D2,D3,D4 trackD;
  class E1,E2,E3,E4 trackE;
  class W0,W1,W2,W3A,W3B,W4,W5,W6 trackW;
  class M0,M1,M2,M3,M4,M5 trackM;
  class R1,R2,R3,R4,R5 trackR;
  class N0,N1,N2,N3,N4,N5,N6,N7,N8,N9,N10,N11,N12,N13,N14,N15,N16,N17,N18,N19,N20 trackN;
  class A4S seam;
```

> Coordination, not ordering: A3 and C1 both touch the presentation seam, and B5 instancing
> should account for C4 (Track C) models. Those are coordination points, not strict dependencies,
> so they are omitted from the arrows above; see the [cross-track dependencies](README.md#cross-track-dependencies)
> note. D3 also depends on the standalone [ModCollector tool](plans/mod-collector.md), which is not
> part of a lettered track and has no engine dependency, so it is omitted as a node. H3 (new map
> storage format) coordinates with B4a-5 (chunked terrain storage) on the map-format terrain
> section; this is likewise a coordination point rather than a strict dependency, so no arrow
> connects them. A4-3 has no separate node, so the A4-3 dependencies of W2 and M4 are drawn from the A4 node.

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
