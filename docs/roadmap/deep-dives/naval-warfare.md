# Deep Dive: Naval Warfare (E1/E2)

> Expands phases E1 and E2 of [Track E — Naval Warfare](../track-e-naval-warfare.md). Part of
> the [Large-Scale Simulation Roadmap](../README.md). First-pass design — refine as it lands.
>
> Audience: human — design intent and architecture diagrams. Code references are in the
> agent-context appendix at the bottom.
>
> The agent-context section at the bottom ([Current code appendix](#appendix-current-code--agent-context))
> contains file:line code references. Skip it if you are reading for design intent.

Goal: make water-surface combat actually work end-to-end. Unlike [Track D](../track-d-gameplay-expansion.md)'s
tech trees/eras, naval warfare is not wholly new to the engine's data model — BFME2's Corsairs of
Umbar content already defines ship `ObjectKind`s (`SHIP`, `SHIP_BATTLESHIP`, `SHIP_BOMBARD`,
`SHIP_SUICIDE`, `SHIP_TRANSPORT`), a `PORT` building kind, and a `WATER` locomotor surface — but
the terrain/pathfinding layer that would make any of it move and fight correctly is an
unimplemented stub. This track finishes that seam and proves it with real ship combat, rather
than inventing new mechanism.

---

## 1. Scope and non-goals

- In scope: real water/ground terrain-passability distinction in the pathfinder (today a stub),
  ship weapon/turret combat (broadside- and bow-mounted), shipyard/port production gated to
  water-adjacent tiles, and a reference scenario proving it end-to-end.
- Out of scope (deferred): full hydrodynamics/buoyancy physics, weather-driven sea states,
  amphibious-assault choreography beyond the existing `AMPHIBIOUS` kind, and any new external
  ship models — E1-E4 validate with the existing BFME2 `SHIP_*` reference content (or W3D
  placeholders) before any Track C/ModCollector content is needed.
- This is content and a missing structural seam within the existing RTS domain — no new
  Simulation Domain Model, consistent with the [domain-scope Decision](../open-questions.md).

---

## 2. Design

### 2.1 Water/ground pathfinding (finishing the stub)

Today, `TerrainLogic.GetLayerForDestination`/`GetHighestLayerForDestination` unconditionally
return `PathfindLayerType.Ground`, and `Pathfinder.ValidMovementTerrain` unconditionally returns
`true` — there is no code path that actually distinguishes water from ground. Meanwhile
`Locomotor.Surfaces` already has a `Water` bit, `AIData` already parses `WadeWaterDepth`/
`DeepWaterDepth`, and `Locomotor` already tracks an `OverWater` flag for visual effects. E1 wires
these together for real:

- `TerrainLogic` gains real per-cell water depth (from the heightmap, versus the existing
  water-plane data already used for rendering) and returns the correct `PathfindLayerType` for a
  given position instead of the hardcoded `Ground`.
- `Pathfinder.ValidMovementTerrain` checks the locomotor's `Surfaces` mask against the cell's
  actual water depth: a `Water`-only locomotor is invalid on dry land; a `Ground`-only locomotor
  is invalid past `DeepWaterDepth` (already parsed, previously unused for this purpose); shallow
  water between `WadeWaterDepth` and `DeepWaterDepth` is enterable by ground units without the
  `Water` surface bit (existing "wading" concept).
- This is a correctness fix to an existing seam (the `PathfindLayerType`/`Locomotor.Surfaces`
  contract already implies this behavior), not a new abstraction — no new engine concept is
  introduced.

### 2.2 Ship combat

Ship `ObjectDefinition`s already carry weapon slots through the existing weapon-set system; E2
adds the two ship-specific gaps:

- Multi-mount broadside weapons — a ship firing several weapons simultaneously from different
  hull-relative bones (port/starboard), reusing the existing multi-weapon-slot mechanism with new
  bone-facing rules rather than a new weapon type.
- Ram/collision damage for `SHIP_SUICIDE`-kind objects — a contact-triggered self-destruct damage
  application, mirroring the existing kamikaze/suicide-unit pattern already used by some land/air
  units (generalized, not naval-specific, so it stays a shared mechanism).

`SHIP_TRANSPORT` (troop-carrying ships) reuses the existing container/garrison transport module
unchanged — the `ObjectKind` flag already exists; E2 only needs to confirm the transport module
does not assume dry-land-only movement (it shouldn't, since containment is orthogonal to
locomotion).

### 2.3 Naval production (`PORT`)

The `PORT` `ObjectKind` building follows the existing production-queue/build-prerequisite
pattern (same as any factory), with one addition: build placement and rally points must resolve
to a water-adjacent (or water) tile, using the E1 terrain-passability data instead of the
land-only placement check the existing construction system uses today.

---

## 3. Sequencing notes

- E1 (pathfinding fix) depends on Track P (a stable playable baseline) so the fix is validated
  against a working match loop, not a partial one — same rationale as Track D's D1/D2. It does
  not depend on Track C or ModCollector; it can be built and tested with BFME2's existing
  `SHIP_*` reference content.
- E2 (ship combat) depends on E1 — weapons/collision are meaningless if ships cannot correctly
  occupy or be constrained to water in the first place.
- E3 (naval production) depends on E1 (water-adjacency placement checks) but not on E2.
- E4 (reference scenario) depends on E2 and E3 — the integration proof, authored/asserted
  through ReplaySketch per the [P0 harness](playable-baseline-harness.md) pattern, same as every
  other phase.

---

## Architecture Diagrams

Placeholder — phase E1/E2 is `Not started`. Per the
[maintaining protocol](../agent/MAINTAINING.md), a class diagram (the `TerrainLogic`/
`PathfindLayerType`/`Pathfinder`/`Locomotor.Surfaces` relationship, plus the ship weapon/transport
module reuse) and a sequence diagram (a move order onto water → pathfinder layer resolution →
locomotor validation → movement) must replace this placeholder before E1's first task card is
marked `Ready`.

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
  condition tracking (visual-only today); E1's terrain data is what should drive this, rather
  than each computing water state independently.
- `src/OpenSage.Game/Logic/AI/AIData.cs:84,105,222,256` — `WadeWaterDepth`/`DeepWaterDepth`
  already parsed from INI but unused for movement validation; E1 is their first real consumer.
- `src/OpenSage.Game/Logic/Object/ObjectKinds.cs` — existing `SHIP`, `SHIP_BATTLESHIP`,
  `SHIP_BOMBARD`, `SHIP_SUICIDE`, `SHIP_TRANSPORT`, `PORT`, `BOAT`, `AMPHIBIOUS` kinds
  (`AddedIn(SageGame.Bfme2)`/`Bfme2Rotwk`) — the reference content E1-E4 validate against; no new
  `ObjectKind` values are needed for the initial reference scenario.
- `src/OpenSage.Game/Logic/Object/Update/DockUpdate.cs` — existing dock/approach-position pattern
  (used today for supply trucks); E3's `PORT` production building placement check follows the
  same module structure, distinct from this supply-specific behavior.
