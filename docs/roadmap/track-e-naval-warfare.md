# Track E: Naval Warfare

> Part of the [Large-Scale RTS Roadmap](README.md). This track completes the water/ground
> movement seam and adds ship combat. It uses existing BFME2 naval content as the reference.
>
> Architecture context: see [`agent/current-state.md`](agent/current-state.md).
>
> Audience: human overview of the naval-warfare track.

## Track ordering rationale

Fixing the water/ground pathfinding stub and adding ship combat both touch core per-object
movement and combat resolution: `Locomotor`, `Pathfinder`, and weapon slots. Like Track D, this
needs a stable, validated match loop in [Track P](track-p-playable-baseline.md), not a partial
one. E2 also consumes F4a's shared Bepu hull/contact path; E1 and E3 can proceed without it. Track
E does not need Track C or [ModCollector](plans/mod-collector.md); the reference BFME2
`SHIP_*`/`PORT` content already ships with the engine's supported games.

## Phases

> The Milestone column states the demonstrable outcome that marks each phase done. Granular steps live in
> [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| E1. Water/ground terrain passability | Implement the currently-stubbed `TerrainLogic`/`Pathfinder` water-vs-ground layer resolution, wiring the already-parsed `Locomotor.Surfaces` (`Water`/`Ground`/`Cliff`) and `AIData.WadeWaterDepth`/`DeepWaterDepth` into real movement validation instead of the unconditional `true`/`Ground` stubs. | A water-only locomotor unit cannot move onto dry land, a ground-only unit cannot enter deep water (but can wade shallow water), and a water unit correctly triggers the existing `OverWater` visual state. | Not started | Track P (P7) |
| E2. Ship combat | Add ship-specific combat: multi-mount broadside weapons plus `SHIP_SUICIDE` ram damage. Use F4a's constrained-dynamic ship body, E1 water height/passability, and shared Bepu contact record instead of a naval collision path. Confirm `SHIP_TRANSPORT` contain/release transitions suspend and restore the passenger body through the same registry. | Two Bepu-backed ships exchange broadside fire without interpenetrating; one contact makes a suicide ship ram and destroy its target exactly once; a transport loads/unloads passengers while moving on water with correct body transitions. | Not started | E1, F4a |
| E3. Naval production (`PORT`) | Wire the existing `PORT` `ObjectKind` building through the standard production-queue/build-prerequisite pattern, with placement/rally-point resolution constrained to water-adjacent tiles via E1's terrain-passability data. | A `PORT` building can only be placed adjacent to water, produces a ship into the production queue, and the ship's rally point resolves correctly on water. | Not started | E1 |
| E4. Reference naval scenario | Author one small water-map scenario (a `PORT` producing a couple of `SHIP_*` reference units that fight) through ReplaySketch, proving E1-E3 work together end-to-end. | A ReplaySketch-authored scenario builds a ship from a port and wins a water engagement, asserted headlessly per the [P0 harness](deep-dives/p0-playable-baseline-harness.md) pattern. | Not started | E2, E3 |

## Phase resources

Navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams are required before any phase's first task card is marked `Ready`. E1/E2 share a deep dive; E3/E4 are still unwritten.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| E1 | [naval-warfare.md](deep-dives/e1-e2-naval-warfare.md) | [Architecture diagrams](deep-dives/e1-e2-naval-warfare.md#architecture-diagrams) | — |
| E2 | [naval-warfare.md](deep-dives/e1-e2-naval-warfare.md) | [Architecture diagrams](deep-dives/e1-e2-naval-warfare.md#architecture-diagrams) | — |
| E3 | — | — | — |
| E4 | — | — | — |

## Cross-track dependencies

- E1/E3 depend only on Track P (P7). E2 additionally depends on F4a's ship participation,
  water-support, filtering, and contact dispatch; E4 inherits that dependency through E2.
- Track E does not require Track C or [ModCollector](plans/mod-collector.md). A later phase could extend it with non-BFME2 naval content, such as Empire Earth-style navies, the same way Track D's D3 ingests external content, but that is outside E1-E4.
- B4a should account for water-layer terrain streaming once both land so chunked/streamed terrain carries the same water-depth data E1 introduces.
- E1 remains pathing authority. F4a/E2 use its water height for vertical support and its valid
  surfaces for route constraints; Bepu does not decide whether a ship may enter land.
