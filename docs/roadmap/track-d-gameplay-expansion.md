# Track D: Gameplay Content Expansion

> Part of the [Large-Scale RTS Roadmap](README.md). This track extends the reference gameplay loop
> beyond the built-in SAGE titles with mechanics from other RTS traditions, starting with
> tech-tree and era/age-progression concepts. It adds gameplay content and mechanism through the
> existing `GameObject`/module system rather than a new Simulation Domain Model.
>
> Architecture context: see [`agent/current-state.md`](agent/current-state.md).
>
> Audience: human overview of the gameplay-expansion track.

## Track ordering rationale

Generalizing `UpgradeTemplate` into an explicit prerequisite graph and adding an era state machine
both touch core per-player game state: `GameLogic`, save/load, the order pipeline, and the control
bar. They need Track P's playable baseline, build → fight → win/lose, so D1-D4 extend a working
game and validate against a working [P0 harness](deep-dives/playable-baseline-harness.md). Track D
does not otherwise depend on Track A/B work. Only D3 onward needs Track C and ModCollector, so D1/D2
can be designed and validated with existing built-in content first.

## Phases

> The Milestone column states the demonstrable outcome that marks each phase done. Granular steps live in
> [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| D1. Tech tree / prerequisite graph generalization | Generalize the existing per-object upgrade/prerequisite mechanism (`UpgradeTemplate`, build/produce prerequisites on `ObjectDefinition`) into an explicit, data-authored prerequisite graph (`TechNode`/`TechTreeState`) that can express branching, non-SAGE tech trees without new engine code per tree. Existing Generals/BFME/BFME2 content keeps working through a thin adapter. | A branching tech tree (at least one node with two alternative prerequisite paths) is authored in INI and gates build/produce availability correctly, with existing built-in content unaffected. | Not started | Track P (P7) |
| D2. Era/age progression mechanic | Add a new per-player `EraState` with a gated age-up action (cost + time-gated, validated through the existing order pipeline, resolved through the existing timed-completion pattern) that unlocks era-gated `TechNode`/`ObjectDefinition` entries on completion. | A player issues an age-up order, it resolves after its time gate, and era-gated build/tech options become available immediately after. | Not started | D1 |
| D3. External content ingestion pipeline | Wire [ModCollector](plans/mod-collector.md)-staged assets (via Track C's import abstraction, at minimum C1) into the `AssetStore` scope-layering system as a distinct content pack, so a downloaded mod's models/INI overrides can back new D1/D2 tech-tree/era content without engine changes per mod. | A ModCollector-staged content pack's models and INI-authored tech nodes load and render through the existing `AssetStore` scope layering, alongside built-in content. | Not started | D1, C1, ModCollector |
| D4. Reference non-SAGE scenario | Author one small end-to-end scenario (a handful of tech-tree nodes plus one era transition, using D3-ingested or placeholder content) through ReplaySketch, proving D1-D3 work together without requiring a full game's worth of content. | A ReplaySketch-authored scenario builds through a branching tech node and one era transition, asserted headlessly per the [P0 harness](deep-dives/playable-baseline-harness.md) pattern. | Not started | D2, D3 |

## Phase resources

Navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams are required before any phase's first task card is marked `Ready`. D1/D2 share a deep dive; D3/D4 are still unwritten.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| D1 | [tech-tree-era-progression.md](deep-dives/tech-tree-era-progression.md) | placeholder† | — |
| D2 | [tech-tree-era-progression.md](deep-dives/tech-tree-era-progression.md) | placeholder† | — |
| D3 | — | — | — |
| D4 | — | — | — |

† Placeholder: see the deep dive's [Architecture Diagrams](deep-dives/tech-tree-era-progression.md#architecture-diagrams)
section. A class + sequence diagram must replace it before D1's first task card is written, per
[`agent/MAINTAINING.md`](agent/MAINTAINING.md).

## Cross-track dependencies

- D1/D2 depend only on Track P (P7). They extend existing per-player game state, not scale/rendering/import infrastructure, and can be designed and validated with existing built-in content before any external asset lands.
- D3 depends on Track C's C1 and on [ModCollector](plans/mod-collector.md), a standalone CLI tool, not part of Tracks A/B/C, planned via the `WI-MC-01..04` agent task cards. It downloads and stages community mod archives into `.big` files that the engine's existing `SkudefReader`/virtual file system can already consume. ModCollector has no engine dependency and can be built any time.
- D4 reuses the [P0 ReplaySketch harness](deep-dives/playable-baseline-harness.md) pattern for authoring and asserting. No new scenario-authoring tool is introduced; see the [C5 tooling Decision](open-questions.md).
- Track D does not require Track B or Track A beyond what Track P already delivers. It is not gated on A1/A4/B1-B7.
