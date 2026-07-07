# Deep Dive: Naval Warfare (E1/E2)

> Expands phases E1 and E2 of [Track E — Naval Warfare](../track-e-naval-warfare.md). Part of
> the [Large-Scale Simulation Roadmap](../README.md).
>
> Audience: human — design intent and architecture diagrams. Code references are in the
> agent-context appendix at the bottom.
>
> The agent-context section at the bottom ([Current code appendix](#appendix-current-code--agent-context))
> contains file:line code references. Skip it if you are reading for design intent.

Goal: make water-surface combat work end-to-end. Unlike [Track D](../track-d-gameplay-expansion.md)'s
tech trees and eras, naval warfare is not wholly new to the engine's data model. BFME2's Corsairs of
Umbar content already defines ship `ObjectKind`s (`SHIP`, `SHIP_BATTLESHIP`, `SHIP_BOMBARD`,
`SHIP_SUICIDE`, `SHIP_TRANSPORT`), a `PORT` building kind, and a `WATER` locomotor surface, but the
terrain/pathfinding layer that should make any of it move and fight correctly is still a stub. This
track finishes that seam and proves it with real ship combat rather than inventing new mechanisms.

---

## 1. Scope and non-goals

- In scope: real water/ground terrain-passability distinction in the pathfinder, ship weapon and
  turret combat (broadside and bow-mounted), shipyard/port production gated to water-adjacent
  tiles, and a reference scenario proving it end-to-end.
- Out of scope, deferred: full hydrodynamics/buoyancy physics, weather-driven sea states,
  amphibious-assault choreography beyond the existing `AMPHIBIOUS` kind, and any new external ship
  models. E1-E4 validate with the existing BFME2 `SHIP_*` reference content, or W3D placeholders,
  before any Track C/ModCollector content is needed.
- This is content and a missing structural seam within the existing RTS domain, not a new
  Simulation Domain Model. See the [domain-scope Decision](../open-questions.md).

---

## 2. Design

### 2.1 Water/ground pathfinding (finishing the stub)

Today, `TerrainLogic.GetLayerForDestination` and `GetHighestLayerForDestination` always return
`PathfindLayerType.Ground`, and `Pathfinder.ValidMovementTerrain` always returns `true`. There is no
code path that distinguishes water from ground. Meanwhile, `Locomotor.Surfaces` already has a
`Water` bit, `AIData` already parses `WadeWaterDepth` and `DeepWaterDepth`, and `Locomotor` already
tracks an `OverWater` flag for visual effects. E1 wires those pieces together:

- `TerrainLogic` gains real per-cell water depth, derived from the heightmap versus the existing
  water-plane data already used for rendering, and returns the correct `PathfindLayerType` for a
  position instead of hardcoded `Ground`.
- `Pathfinder.ValidMovementTerrain` checks the locomotor's `Surfaces` mask against the cell's real
  water depth. A `Water`-only locomotor is invalid on dry land. A `Ground`-only locomotor is invalid
  past `DeepWaterDepth`, already parsed but unused for this purpose. Shallow water between
  `WadeWaterDepth` and `DeepWaterDepth` remains enterable by ground units without the `Water`
  surface bit.
- This is a correctness fix to an existing seam. The `PathfindLayerType`/`Locomotor.Surfaces`
  contract already implies this behavior, so no new engine concept is introduced.

### 2.2 Ship combat

Ship `ObjectDefinition`s already carry weapon slots through the existing weapon-set system. E2 adds
the two missing ship-specific pieces:

- Multi-mount broadside weapons: a ship fires several weapons simultaneously from different
  hull-relative bones on port and starboard sides, reusing the existing multi-weapon-slot mechanism
  with new bone-facing rules rather than a new weapon type.
- Ram/collision damage for `SHIP_SUICIDE` objects: a contact-triggered self-destruct damage
  application that mirrors the existing kamikaze/suicide-unit pattern already used by some land and
  air units. This is generalized shared mechanism, not naval-only behavior.

`SHIP_TRANSPORT` reuses the existing container/garrison transport module unchanged. The
`ObjectKind` flag already exists. E2 only needs to confirm the transport module does not assume
land-only movement, which it should not because containment is orthogonal to locomotion.

### 2.3 Naval production (`PORT`)

The `PORT` `ObjectKind` building follows the existing production-queue/build-prerequisite pattern,
like any factory, with one addition: build placement and rally points must resolve to a water-adjacent
or water tile by using E1's terrain-passability data instead of today's land-only placement check.

---

## 3. Sequencing notes

- E1 depends on Track P so the pathfinding fix is validated against a working match loop rather than
  a partial one. It does not depend on Track C or ModCollector and can be built and tested with
  BFME2's existing `SHIP_*` reference content.
- E2 depends on E1. Weapons and collision are meaningless if ships cannot correctly occupy or be
  constrained to water first.
- E3 depends on E1 because it needs water-adjacency placement checks, but not on E2.
- E4 depends on E2 and E3. It is the integration proof, authored and asserted through ReplaySketch
  using the [P0 harness](playable-baseline-harness.md) pattern.

---

## Architecture Diagrams

Placeholder — phase E1/E2 is `Not started`. Per the [maintaining protocol](../agent/MAINTAINING.md),
a class diagram covering the `TerrainLogic`/`PathfindLayerType`/`Pathfinder`/
`Locomotor.Surfaces` relationship plus ship weapon and transport-module reuse, and a sequence
diagram covering move order onto water → pathfinder layer resolution → locomotor validation →
movement, must replace this placeholder before E1's first task card is marked `Ready`.

---

## Appendix: current code / agent context

- `src/OpenSage.Game/Terrain/TerrainLogic.cs:88,97` — `GetLayerForDestination`/
  `GetHighestLayerForDestination` unconditionally return `PathfindLayerType.Ground`; this is the
  stub E1 replaces with real water/ground resolution.
- `src/OpenSage.Game/Logic/AI/Pathfinder.cs:22-26` — `ValidMovementTerrain` unconditionally
  returns `true` (`// TODO(Port): Implement this.`); this is the stub E1 implements against
  locomotor surface + water depth.
- `src/OpenSage.Game/Logic/Object/LocomotorTemplate.cs:655-670` — `Surfaces` enum already has
  `Ground`/`Water`/`Cliff`/`Air` bits; E1 makes `Water` actually constrain movement.
- `src/OpenSage.Game/Logic/Object/Locomotor.cs:1336-1354` — existing `OverWater` flag/model
  condition tracking, visual-only today; E1's terrain data should drive this rather than each side
  computing water state independently.
- `src/OpenSage.Game/Logic/AI/AIData.cs:84,105,222,256` — `WadeWaterDepth`/`DeepWaterDepth`
  already parsed from INI but unused for movement validation; E1 is their first real consumer.
- `src/OpenSage.Game/Logic/Object/ObjectKinds.cs` — existing `SHIP`, `SHIP_BATTLESHIP`,
  `SHIP_BOMBARD`, `SHIP_SUICIDE`, `SHIP_TRANSPORT`, `PORT`, `BOAT`, `AMPHIBIOUS` kinds
  (`AddedIn(SageGame.Bfme2)`/`Bfme2Rotwk`), the reference content E1-E4 validate against. No new
  `ObjectKind` values are needed for the initial reference scenario.
- `src/OpenSage.Game/Logic/Object/Update/DockUpdate.cs` — existing dock/approach-position pattern
  used today for supply trucks; E3's `PORT` production building placement check follows the same
  module structure, distinct from this supply-specific behavior.
