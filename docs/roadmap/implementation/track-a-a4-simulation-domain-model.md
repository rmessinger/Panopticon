# Implementation Plan: Track A · A4-1 Simulation Domain Model Seam

> Checklist item: A4-1 (domain-agnostic entities/topology/interactions/metrics).
> Source of truth: [deep-dives/a4-simulation-domain-model.md](../deep-dives/a4-simulation-domain-model.md).
> This file records the WI-A4-05 audit findings; the deep dive holds the design.

## Audit findings (WI-A4-05)

Every A4-1 responsibility, its current-code owner, and its classification. Classification:
domain contract (shared, domain-agnostic), RTS adapter implementation (RTS-specific behind the
future contract), or host concern (stays in `SimulationHost`/recording regardless of domain).

### Entity schema

| Assumption | Current owner | Classification |
|---|---|---|
| Entities are `GameObject`s: modules + `ObjectDefinition` | `Logic/Object/GameObject.cs`, module base types | RTS adapter (the shared entity runtime; non-RTS domains extend it with modules per the domain-scope Decision) |
| Recording needs identity, definition, owner, position, health | `Recording/WorldStateSnapshot.cs` `WorldObjectState` (ObjectId, DefinitionName, OwnerIndex, Position, Health, MaxHealth) | Domain contract shape: the summary lists entities with stable ids and a small state vector; the specific fields are the RTS adapter's projection |
| Object ids are stable and ordered | `GameLogic` iterates id order; `WorldStateSnapshot` relies on it | Domain contract (stable entity keys) |
| Players own money and objects | `RunPlayerMetrics` reads `Player.BankAccount`, `Player.OwnedObjects` (`RunOutput.cs`) | RTS adapter projection (domain metrics own their state sources) |

### Topology / space model

| Assumption | Current owner | Classification |
|---|---|---|
| World is a 2-D spatial grid for partitioning | `Logic/PartitionCellManager.cs` (numCellsX/Y, cell size, `GetCells(position, radius)`) | RTS adapter; the domain contract is topology keys + adjacency |
| T1 bucketing anchors a square grid at world origin | `Recording/SignatureTree.cs` `SignatureTreeBuilder` (floor(pos/cellSize), key `cell:x,y`) | RTS adapter; comment already names the domain seam as the successor |
| Spatial queries use a quadtree | `IGameEngine.Quadtree` (`IQuadtree<GameObject>`) | RTS adapter detail |
| Terrain heightmap bounds the world | `IGameEngine.Terrain` | RTS adapter detail |

### Interaction model & locality

| Assumption | Current owner | Classification |
|---|---|---|
| Locality derives from radius over the grid | `PartitionCellManager.GetCells(Vector2, float)` halos; weapons/vision ranges | RTS adapter; the domain contract expresses locality as topology adjacency (cells today, graph edges or zone borders for other domains) |

### Interaction-resolution authority

| Assumption | Current owner | Classification |
|---|---|---|
| Single process resolves everything; "initiator's shard" is implicit | `Logic/GameLogic.cs` update loop (sequential, single authority) | Host concern today; the domain contract must state the rule (RTS: initiator resolves; graph: edge owner; flows: conservation at the cut) so B6 can shard without re-deriving it |

### Domain metrics / probes

| Assumption | Current owner | Classification |
|---|---|---|
| T0 metrics are money + owned-object counts per player | `RunOutputRecorder` (`RunOutput.cs`): `RunPlayerMetrics` | RTS adapter projection; the contract is "the domain supplies named metric series" |
| Outcome recorded from `GameOutcome` | `RunOutputRecorder.Complete()` reads `IGame.CurrentOutcome` | Host concern (generic record) fed by an RTS-shaped source |

### Recording buckets

| Assumption | Current owner | Classification |
|---|---|---|
| T1 level-1 nodes are spatial cells `cell:x,y` | `SignatureTreeBuilder.Build` | RTS adapter; graph domains need `edge:*` keys |
| Entity signature hashes position/health quantized | `SignatureTreeBuilder.EntitySignature` | Domain contract (quantized tolerance hashing); the quantized fields are the entity summary projection |
| Snapshots/deltas are summaries of the same shape | `WorldStateSummary`/`WorldStateDelta` | Domain contract shape shared by all tiers |

### Visualization projection

| Assumption | Current owner | Classification |
|---|---|---|
| A5 reads one JSON run-output format (camelCase, string enums) | `RunOutput.JsonOptions`, P0 harness conventions | Host concern (format stability); domain-parameterized views come from the domain's metric/probe and bucket-key contracts |

### Fidelity hooks

| Assumption | Current owner | Classification |
|---|---|---|
| None exist yet; no LOD/aggregation switch points | absent | Domain contract to define (empty v1: identity fidelity), consumed by B7 later |

### Host separation summary

`SimulationHost` (`SimulationHost.cs`) is already domain-agnostic in stepping, order
scheduling, and events. Its RTS coupling is exactly one: the constructor takes
`IGameEngine`, whose `GameLogic`/`Terrain`/`Quadtree` surface is RTS-shaped (noted in its
comment at `SimulationHost.cs:84`). The recording stack couples in three places:
`WorldStateSummary.Capture` (entity projection), `RunOutputRecorder` (metric projection),
and `SignatureTreeBuilder` (bucket projection). These five surfaces are the extraction
targets; nothing else in the host or recorder needs domain knowledge.

## Minimal transport/graph validation scope (WI-A4-05 step 3)

The smallest behavior that exercises every contract above:

1. A small directed graph (about 6 nodes, 7 edges) as the topology; stable keys `node:i`,
   `edge:i->j`.
2. Entities: vehicles represented with the shared `GameObject`/module runtime (definition with
   no render/audio coupling), moving along edges.
3. Edge-coupled interaction: a transfer at an edge tail crossing a designated partition cut
   (edge owner authority: the transfer resolves exactly once, on the owner side).
4. Boundary metrics: per-edge throughput counts (vehicles crossing) plus per-node occupancy.
5. T1 recording with `edge:*` bucket keys and identical quantization/tolerance machinery.
6. Terminating: a fixed number of vehicles reach their destination nodes, then the run stops.

Out of scope: routing product, passenger modeling, economy, signals, full scenarios
(the non-RTS completeness Decision).

## Ambiguities recorded for WI-A4-06

- Whether the domain contract carries the entity summary projection (per-field state vector)
  or the recording reads a domain-provided summary interface: both are one method; the design
  picks projection-by-domain (contract supplies the summary) so recorders never branch on
  domain type.
- Whether metrics are pull (recorder samples the domain) or push (domain emits): existing
  recorders are pull/event-shaped, so pull with the domain naming its series.
- Where the domain model attaches to `SimulationHost` versus `IGameEngine`: the host already
  owns stepping, so the domain model wraps the engine's world state for projection while the
  engine stays the entity runtime.
