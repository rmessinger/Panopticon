# Track E — Naval Warfare

> Part of the [Large-Scale RTS Roadmap](README.md). Makes water-surface combat actually work.
> Unlike [Track D](track-d-gameplay-expansion.md)'s tech trees/eras, naval warfare is not wholly
> new content — BFME2's Corsairs of Umbar data already defines ship `ObjectKind`s and a `WATER`
> locomotor surface — but the terrain/pathfinding layer that would make any of it move and fight
> correctly is an unimplemented stub (`Pathfinder.ValidMovementTerrain` always returns `true`,
> `TerrainLogic` always resolves to the ground layer). This track finishes that seam and proves
> it with real ship combat.
>
> Audience: human — strategic track overview.

## Why this comes after Track P

Fixing the water/ground pathfinding stub and adding ship combat both touch core per-object
movement and combat resolution (`Locomotor`, `Pathfinder`, weapon slots) — the same reasoning as
Track D: this needs a stable, validated match loop ([Track P](track-p-playable-baseline.md)) to
build and test against, not a partial one. Track E does not otherwise depend on Track A/B (scale)
work, and does not need Track C or [ModCollector](plans/mod-collector.md) at all — the reference
BFME2 `SHIP_*`/`PORT` content already ships with the engine's supported games.

## Phases

> The Milestone column states the single demonstrable outcome that marks each phase done (its
> definition-of-done); the granular steps to get there live in
> [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| E1. Water/ground terrain passability | Implement the currently-stubbed `TerrainLogic`/`Pathfinder` water-vs-ground layer resolution, wiring the already-parsed `Locomotor.Surfaces` (`Water`/`Ground`/`Cliff`) and `AIData.WadeWaterDepth`/`DeepWaterDepth` into real movement validation instead of the unconditional `true`/`Ground` stubs. | A water-only locomotor unit cannot move onto dry land, a ground-only unit cannot enter deep water (but can wade shallow water), and a water unit correctly triggers the existing `OverWater` visual state. | Not started | Track P (P7) |
| E2. Ship combat | Add ship-specific combat: multi-mount broadside weapons (reusing the existing weapon-slot mechanism with hull-relative bone facing) and ram/collision damage for `SHIP_SUICIDE`-kind objects (generalizing the existing kamikaze/suicide-unit pattern). Confirm `SHIP_TRANSPORT` reuses the existing container/garrison module unchanged. | Two ships exchange broadside fire and destroy each other; a suicide ship rams and destroys a target on contact; a transport ship loads/unloads passengers while moving on water. | Not started | E1 |
| E3. Naval production (`PORT`) | Wire the existing `PORT` `ObjectKind` building through the standard production-queue/build-prerequisite pattern, with placement/rally-point resolution constrained to water-adjacent tiles via E1's terrain-passability data. | A `PORT` building can only be placed adjacent to water, produces a ship into the production queue, and the ship's rally point resolves correctly on water. | Not started | E1 |
| E4. Reference naval scenario | Author one small water-map scenario (a `PORT` producing a couple of `SHIP_*` reference units that fight) through ReplaySketch, proving E1-E3 work together end-to-end. | A ReplaySketch-authored scenario builds a ship from a port and wins a water engagement, asserted headlessly per the [P0 harness](deep-dives/playable-baseline-harness.md) pattern. | Not started | E2, E3 |

## Phase resources

A navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams are required before any phase's first task card is marked `Ready`; E1/E2 share a deep
dive, E3/E4 are yet to be written.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| E1 | [naval-warfare.md](deep-dives/naval-warfare.md) | placeholder† | — |
| E2 | [naval-warfare.md](deep-dives/naval-warfare.md) | placeholder† | — |
| E3 | — | — | — |
| E4 | — | — | — |

† Placeholder — see the deep dive's [Architecture Diagrams](deep-dives/naval-warfare.md#architecture-diagrams)
section; a class + sequence diagram must replace it before E1's first task card is written, per
[`agent/MAINTAINING.md`](agent/MAINTAINING.md).

## Cross-track dependencies

- E1-E4 depend on Track P (P7) only — they finish an existing structural stub and exercise
  content the engine already ships with BFME2, so no external asset pipeline is required.
- Track E does not require Track C or [ModCollector](plans/mod-collector.md); a later phase could
  extend it with non-BFME2 naval content (e.g. Empire Earth-style navies) the same way Track D's
  D3 ingests external content, but that is not part of E1-E4's scope.
- B4a (larger maps) should account for water-layer terrain streaming once both land — coordinate
  so chunked/streamed terrain carries the same water-depth data E1 introduces.
- Track E does not require Track A/B beyond what Track P already delivers; it is not gated on
  A1/A4/B1-B7.
