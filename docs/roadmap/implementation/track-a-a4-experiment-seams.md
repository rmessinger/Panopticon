# Implementation plan: Track A · A4 experiment seams

> Audience: human + agent implementation plan for the A4 seam extraction audit.

Source of truth: [seam registry](../seams.md). This file records the WI-A4-31 audit:
construction and caller sites, reference implementations, minimum A4 extraction boundaries,
and unresolved contracts for the five seams that WI-A4-32..36 extract. The owning B/C-track
designs define the final contracts; this audit only fixes the extraction points.

## Card sequence

| Card | Output | Status |
|---|---|---|
| [`WI-A4-31`](../agent/cards/WI-A4-31-audit-experiment-seam-ownership.md) | This audit | Ready |
| [`WI-A4-32`](../agent/cards/WI-A4-32-extract-pathfinding-seam.md) | Pathfinding/routing seam | Blocked (B4) |
| [`WI-A4-33`](../agent/cards/WI-A4-33-extract-entity-storage-seam.md) | Entity-storage seam | Blocked (B3) |
| [`WI-A4-34`](../agent/cards/WI-A4-34-extract-partition-scheme-seam.md) | Partition-scheme seam | Blocked (B6-1) |
| [`WI-A4-35`](../agent/cards/WI-A4-35-extract-fidelity-policy-seam.md) | Fidelity-policy seam | Blocked (B7-1) |
| [`WI-A4-36`](../agent/cards/WI-A4-36-register-model-import-seam.md) | Model-import seam registration | Ready |

## Seam audit

### Pathfinding / routing (WI-A4-32)

- Construction: `Scene3D.cs:313` constructs `Navigation.Navigation(mapFile.BlendTileData,
  Terrain.HeightMap)` per map load; no alternative implementation exists.
- Callers: `AIUpdate.cs:209` is the only path consumer
  (`GameEngine.Navigation.CalculatePath(start, targetPoint, out var endIsPassable)`); the M0
  deployment reachability check calls `Navigation.IsPassable` outside the logic tick.
- Reference implementation: A* over the per-map nav graph
  (`src/Panopticon.Simulation/Navigation/{Graph,Node,Navigation,PathOptimizer}.cs`).
- Owning phase for alternatives: B4 defines the routing contract
  (hierarchical/flow-field, `track-b-scale.md`); B6-5 adds cross-shard routing.
- Minimum A4 boundary: one interface at the `Navigation` construction site, selected per run,
  exposing `CalculatePath` and `IsPassable`. `GameEngine.Navigation` stays the consumer-facing
  property; callers do not change.
- Unresolved contracts (B4): request/cancellation semantics for army-scale group moves,
  path-request cost accounting (B1 counter), and async or batched query shapes. B4 must
  populate its diagrams before WI-A4-32 starts.

### Entity storage layout (WI-A4-33)

- Construction: entities are `GameObject` plus module objects created through
  `IGameObjectCollection.CreateObject` (`Logic/Object/IGameObjectCollection.cs:8`),
  implemented at `GameLogic.cs:94`; `Scene3D.cs:475` creates starting objects at map load.
- Callers: all gameplay code iterates `GameObject` and its module properties directly; no
  storage interface exists between logic and layout.
- Reference implementation: module objects (today's engine behavior).
- Owning phase for alternatives: B3 defines data-oriented hot-data storage and its invariants.
- Minimum A4 boundary: the `CreateObject` funnel and the iteration surfaces used by
  `PartitionCellManager` and `GameLogic` object enumeration. The extraction must not change
  `GameObject`'s public surface; B3 decides what moves behind the seam.
- Unresolved contracts (B3): persistence/replay invariants for a new layout (A1-5 run outputs
  must stay reconstructable), intent-buffer conflict resolution ordering, and which module
  hot data may be denormalized. B3 must define storage invariants and populate its diagrams
  before WI-A4-33 starts.

### Partition scheme (WI-A4-34)

- Construction: `PartitionCellManager` (`Logic/PartitionCellManager.cs:10`, internal ctor at
  `:31`) is created with the game; the render-side `Quadtree<GameObject>` is constructed at
  `Scene3D.cs:336`.
- Callers: `GameLogic.cs:146` and `:213` forward object add/remove;
  `Player.cs:1018`/`:1062` (supply distances), `ConstructBuildingOrderGenerator.cs:117`,
  `WorkerAIUpdate.cs:98`/`:119`, `FoundationAIUpdate.cs:41`, `PassiveAreaEffectBehavior.cs:31`
  (proximity queries), `Locomotor.cs:641` (ground height). The quadtree is exposed through
  `IScene3D.cs:40` / `IGameEngine.cs:39` / `GameEngine.cs:59`/`:74`.
- Reference implementation: the spatial grid cell manager.
- Owning phase for alternatives: B6-1 defines `IPartitionScheme` domain contracts, halo
  derivation, and boundary message formats (deep dive §4).
- Minimum A4 boundary: the proximity-query and add/remove surface used by the callers above;
  the seam is carved from `PartitionCellManager`, not from the render quadtree.
- Unresolved contracts (B6-1): `IPartitionScheme` is a domain contract
  (`b6-distributed-simulation.md` §4), so the interface shape, halo derivation, and boundary
  messages must be implementation-ready before WI-A4-34 starts.

### Fidelity policy (WI-A4-35)

- Construction: fidelity is domain-supplied: `ISimulationDomainModel.Fidelity`
  (`Simulation/ISimulationDomainModel.cs:20`) returns a `DomainFidelityPolicy`; the RTS
  reference returns `DomainFidelityPolicy.Identity`
  (`Simulation/RtsSimulationDomainModel.cs:22`).
- Callers: no logic caller consults fidelity yet; the seam exists as a hook only.
- Reference implementation: identity (full fidelity).
- Owning phase for alternatives: B7-1 defines `SimulationFidelityLevel`, `FidelityState`, and
  `FidelityRegistry` (`WI-B7-01..03`); B7-4 generalizes group aggregation from W2.
- Minimum A4 boundary: policy selection next to `ISimulationDomainModel.Fidelity`; no caller
  changes beyond consulting the policy where B7 directs.
- Unresolved contracts (B7-1/B7-4): the replicated-state policy input contract (policies
  classify from shared simulation state only, `WI-B7-02`), registry projection into domain
  state and metrics (`WI-B7-03`), and the group aggregation contract (`WI-B7-04`). These must
  be implementation-ready before WI-A4-35 starts.

### Model / animation import (WI-A4-36)

- Construction: `OnDemandAssetLoadStrategy` (`Content/OnDemandAssetLoadStrategy.cs:8`) holds
  the `IModelImporter`/`IAnimationImporter` pair (internal properties at `:44`/`:47`);
  `CreateW3d` (`:22`) is the shipped factory; mod assemblies use it so importer contracts stay
  internal to `Panopticon.Simulation`.
- Callers: `Content/Loaders/OnDemand{Model,ModelBoneHierarchy,Animation}Loader` take importers
  in their constructors; asset resolution goes through the C1 interchange types.
- Reference implementation: W3D (`Content/Importing/W3d/W3dModelImporter.cs:18`,
  `W3dAnimationImporter.cs:15`). Proven alternatives exist behind the same interfaces: glTF
  (C4), NIF 10.1 (C4a and later EE2 phases), SC2 M3 (C7, planned).
- Owning phase: C1 owns the importer contracts and interchange types (landed, diagrams
  populated). No B-track dependency.
- Minimum A4 boundary: registration of importer selections in the A4 experiment registry
  (WI-A4-38) so an experiment can bind glTF or NIF import without caller changes; the
  factory seam itself already exists and needs no widening.
- Unresolved contracts: none blocking. WI-A4-36 waits only on WI-A4-38 for the registry.

## A4 extraction vs. later implementations

The A4 cards introduce interfaces and selection only. The optimized implementations behind
those interfaces are owned by the B/C phases and land as their own cards:

| Seam | A4 delivers | Implementation owned by |
|---|---|---|
| Pathfinding | selection interface at `Navigation` construction | B4 (single-node), B6-5 (cross-shard) |
| Entity storage | storage seam behind `CreateObject` | B3 |
| Partition | `PartitionCellManager`-derived query seam | B6-1..B6-5 |
| Fidelity | policy selection beside the domain model | B7-1 (model/registry), B7-4 (aggregation) |
| Model import | registry bindings over the existing C1 factory | C1 (done), C4/C7 importers |

## Validation

```sh
git status --short -- 'src/*'
```

WI-A4-31 changes no source files.
