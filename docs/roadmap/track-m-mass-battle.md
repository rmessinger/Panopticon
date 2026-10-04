# Track M: Mixed-Roster Mass Battle

> Part of the [Large-Scale RTS Roadmap](README.md). This track turns the
> [1000 vs 1000 battle charter](deep-dives/mass-battle-1k.md) into staged demonstrations: an AI
> versus AI battle of 1000 Generals and Empire Earth II units per side on a large map, run headless
> in batches and viewed live in real time.
>
> Architecture context: see [`agent/current-state.md`](agent/current-state.md).
>
> Audience: human overview of the mass battle track.

## Track ordering rationale

M0 selects the battle map first, so every later measurement and scenario runs on the real
battlefield. M1 then measures the current engine with Generals-only armies on that map before any
new content exists, so later phases target measured bottlenecks. M2 is independent of M0 and M1 and
can start in parallel. M3 needs EE2 units from M2; M4 needs the M1 probe. M5 integrates everything and owns the live-view milestone.

## Phases

> The Milestone column states the demonstrable outcome that marks each phase done. Granular steps
> live in [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| M0. Battle map | Source a large community Generals map, or author one with B4a if none passes, against the [map requirements](deep-dives/mass-battle-1k.md#4-map). | The map loads headless and windowed; both deployment zones hold 1000 units with airfields behind them; a reachability check connects the zones for every ground locomotor; provenance is recorded. | In progress ([WI-M0-01](agent/cards/WI-M0-01-map-evaluation.md) done) | P9, A1 |
| M1. Capacity probe | Direct-spawn 1000 Generals ground units and aircraft per side, with pre-placed airfields, on the M0 map and order a general attack, headless and windowed. | A B1 report records tick cost, frame rate, memory, and path-request load over the engagement and names the first failing constraint. | Not started | M0, B1 |
| M2. EE2 combat units | Read EE2 `.ddf` unit definitions and translate ground combat units and aircraft into scoped `ObjectDefinition`s with C4a-C4d models and animations. | Each reference EE2 unit spawns, moves, attacks, and dies in the P9 validation ground beside Generals units; EE2 aircraft rearm at an airfield. | Not started | C4d, P9 |
| M3. Mixed roster normalization | Normalize EE2 model scale, distances, and damage classes to Generals conventions, add a cross-game points budget, and author two asymmetric roster manifests. | Two different 1000-unit armies build within the points tolerance; a cross-game duel matrix stays within authored bounds. | Not started | M2 |
| M4. Army-scale AI | Run the A4-3 hierarchy over 1000 units per side with squad-level group orders and an air commander for aircraft missions and rearm. | Every unit stays assigned to a squad through casualties; per-tick AI cost stays within budget; no idle-unit stalls in an M1-scale run. | Not started | M1, A4 |
| M5. 1000 vs 1000 battle | Run the mixed-roster battle on the M0 map with M4 AI as headless batches, A5 replays, and a live windowed view. | Seeded batch runs reach a P3 outcome and report per-side win rates with confidence intervals; a mirrored control shows no map bias; replays scrub; the live view sustains a 30 fps median with the battle in view. | Not started | M0, M3, M4, B4, B5, A5 |

## Phase resources

All phases share one deep dive. Diagrams are required before any phase's first source-edit card is
marked `Ready`. `†` marks conceptual diagrams awaiting grounding.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| M0 | [Map](deep-dives/mass-battle-1k.md#4-map) | [§9](deep-dives/mass-battle-1k.md#9-architecture-diagrams) `†` | — |
| M1 | [Capacity probe](deep-dives/mass-battle-1k.md#7-scale-dependencies) | [§9](deep-dives/mass-battle-1k.md#9-architecture-diagrams) `†` | — |
| M2 | [Rosters](deep-dives/mass-battle-1k.md#5-rosters) | [§9](deep-dives/mass-battle-1k.md#9-architecture-diagrams) `†` | — |
| M3 | [Rosters](deep-dives/mass-battle-1k.md#5-rosters) | [§9](deep-dives/mass-battle-1k.md#9-architecture-diagrams) `†` | — |
| M4 | [AI](deep-dives/mass-battle-1k.md#6-ai) | [§9](deep-dives/mass-battle-1k.md#9-architecture-diagrams) `†` | — |
| M5 | [Viewing model](deep-dives/mass-battle-1k.md#3-viewing-model) | [§9](deep-dives/mass-battle-1k.md#9-architecture-diagrams) `†` | — |

## Cross-track dependencies

- B4 and B5 are M5 dependencies; M1 decides whether B2/B3 are also needed at 2000 units.
- B4a is needed only if M0 authors the map; M0 then also depends on B4a.
- M2 consumes the Track C EE2 importers; C4h particle effects are optional polish, not a gate.
- B5 instancing must cover Track C models, as already noted under Track B.
- The track does not use B6, B7, or any Track W domain code.

## Post-track direction

Operator note (2026-10-04): after this track completes, map development starts as its own
effort, creating large maps programmatically (generation and authoring tooling for
mass-battle-scale battlefields) instead of sourcing community maps. Track M stays on the
sourced `heavyfire` map; the deferred M0-2 authored fallback folds into that later effort.
