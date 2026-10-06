# Track H: Map & Unit Storage Formats

> Part of the [Large-Scale RTS Roadmap](README.md). A new on-disk storage system for maps and
> unit/object template data: a format-agnostic interchange abstraction, a versioned successor to
> the `.map` format, a versioned successor to per-game INI object-definition storage, and the
> migration/tooling to move existing content onto it. H2a adds the shared multi-source unit-content
> import layer before M2. The remaining phases are a persistence and schema-design
> concern, distinct from [B3](track-b-scale.md) (data-oriented runtime layout for unit/`GameObject`
> state) and [B4a-5](deep-dives/b4a-larger-maps.md) (chunked terrain runtime storage for scale);
> see the [scope Decision](open-questions.md) and [cross-track dependencies](#cross-track-dependencies).
>
> Audience: human overview of the storage-formats track.

## Phases

> The Milestone column states the single demonstrable outcome that marks each phase done (its
> definition-of-done); the granular steps to get there live in
> [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| H1. Format requirements & current-state audit | Audit today's on-disk storage for maps (`.map` binary format: `MapFile`, `HeightMapData`, `BlendTileData`, `ObjectsList`) and object/unit templates (per-game `.ini` text: `IniParser`, `ObjectDefinition`, `AssetStore`), documenting concrete limitations for Panopticon-authored content: no schema-versioning story beyond ad hoc version integers, no diff/merge-friendly text form for maps, mod content that collides by editing shared files rather than composing, and no validation step that catches malformed content before load. Settle the format-design questions the rest of this track depends on: read-only legacy compatibility vs. read/write, human-editable vs. binary, and whether maps and object definitions share one container format or stay separate. | A written audit lists every current-state limitation with a file:line citation, and each format-design question has a recorded Decision in `open-questions.md`. | Not started | — |
| H2. Format-agnostic map interchange abstraction | Define engine-internal interchange types for terrain, blend/texture tiles, and placed-object references, plus a map-format seam behind the existing loading path. Refactor the legacy `.map` reader as the reference implementation. Unit-content interchange belongs to H2a. | The legacy `.map` reader loads through the new map seam with no behavior change. | Not started | H1 |
| H2a. Unit-content import layer | Add source adapters, engine-owned unit-content interchange, inheritance and dependency resolution, capability validation, and shared runtime materialization; see the [design scaffold](deep-dives/h2a-unit-content-import.md). Route Generals INI through the seam and prove a bounded EE2 slice. | Generals retains existing unit behavior through the shared path; a real EE2 ground unit resolves health, movement, weapons, armor, and visual states through that path and passes P9 beside Generals; failed imports publish no partial content. | In progress (WI-H2a-01..18 done; WI-H2a-19 open) | P9, C4d |
| H2b. Metric core units | Convert the engine's internal length unit from legacy feet to meters, normalizing once at every source boundary and inverting on legacy writes; see the [design scaffold](deep-dives/h2b-metric-core.md). Starts after the complete H2a group merges. | Runtime lengths, rates, and model/terrain geometry are metric; legacy maps, W3D, INI, replays, and saves load and round-trip; Generals and EE2 P9 proofs match final-H2a behavior scaled by `0.3048`. | Not started | H2a |
| H3. New map storage format | Design and implement a new on-disk map format behind the H2 seam: versioned, schema-evolvable, and structured so a terrain payload section can host [B4a-5's chunked heightmap representation](deep-dives/b4a-larger-maps.md) without a second, competing map-format effort. Scope here is the container, schema, and versioning; B4a-5 owns the chunked terrain data model and streaming behavior that fills the terrain section. | A map authored in the new format round-trips (write, reload) with terrain, blend, and placed-object data intact, and reserves a terrain section B4a-5 can adopt for chunked storage. | Not started | H2 |
| H4. New object/unit-definition storage format | Design and implement a new storage adapter over H2a's unit-content representation, including weapons, upgrades, locomotors, and F4a engine-level physics participation/material/filter overrides, with explicit schema versioning and forward-compatible field addition. Keep INI as a permanent legacy read path and keep Bepu types/handles out of the schema. | A unit authored in the new format loads with full parity to an equivalent INI-authored unit, including resolved physics metadata, and adding a new field does not break older content. | Not started | H2a |
| H5. Legacy migration & compatibility tooling | Build conversion tooling that reads legacy `.map` and INI content and emits the new H3/H4 formats, plus a round-trip validator that confirms converted content is behaviorally equivalent to the original. | A converted legacy map and unit roster load through the new formats and play identically (per the P0 assertion layer) to loading the original `.map`/INI content. | Not started | H3, H4 |
| H6. Authoring & pipeline integration | Wire WorldBuilder (map authoring) and the modding/content pipeline (ModCollector staging, `AssetStore` scope layering used by [Track D](track-d-gameplay-expansion.md)) to read and write the new formats, preserving today's fast INI edit/hot-reload iteration loop for object definitions during development. | WorldBuilder authors, saves, and reloads a map in the new format, and a ModCollector-staged content pack's object definitions load through the new format alongside built-in content. | Not started | H5 |
| H7. Validation & round-trip regression | Add automated round-trip regression coverage: load legacy, convert, reload, compare for a representative map and unit roster, plus a ReplaySketch-authored scenario that builds and fights using new-format content, asserted headlessly per the P0 harness pattern. | A regression suite catches a round-trip mismatch for maps or object definitions, and a ReplaySketch scenario using new-format content passes the same headless assertions as its legacy-format equivalent. | Not started | H5, H6 |

## Phase resources

A navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Grounded diagrams are required before a phase's first source-edit card is marked `Ready`.
H2a has a design scaffold; its audit card supplies the evidence for the architecture gate.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| H1 | Written by `WI-H1-01` | — | [`WI-H1-01`](agent/cards/WI-H1-01-map-format-audit.md), [`WI-H1-02`](agent/cards/WI-H1-02-format-decision-options.md) |
| H2 | Extended by `WI-H2-01` | — | [`WI-H2-01`](agent/cards/WI-H2-01-map-interchange-design.md) |
| H2a | [Unit-content import](deep-dives/h2a-unit-content-import.md) | [Architecture gate](deep-dives/h2a-unit-content-import.md#architecture-diagrams) `†` | [WI-H2a-01..19](deep-dives/h2a-unit-content-import.md#card-sequence) |
| H2b | [Metric core units](deep-dives/h2b-metric-core.md) | [Architecture gate](deep-dives/h2b-metric-core.md#architecture-diagrams) `†` | [WI-H2b-01..18](deep-dives/h2b-metric-core.md#card-sequence) |
| H3 | [Plan](implementation/track-h-h1-h3-map-storage.md) | — | `WI-H3-01..04` |
| H4 | — | — | — |
| H5 | — | — | — |
| H6 | — | — | — |
| H7 | — | — | — |

## Cross-track dependencies

Hard dependencies are in the phase table above; these are the coordination notes:

- H2a runs next before M2, independently of H1, H2, and H3. Its focused unit-loading audit is `WI-H2a-01`; it does not wait for map/storage format decisions.
- H2a owns unit-content resolution and materialization; Track C supplies visual assets, M2 expands the EE2 adapter and roster, and M3 owns cross-game balancing. H4 serializes the shared representation rather than inventing another unit schema.
- Track H's storage phases design on-disk format, versioning, and
  tooling. It does not re-architect runtime storage, which stays owned by
  [B3](track-b-scale.md) (unit/`GameObject` in-memory layout) and
  [B4a-5](deep-dives/b4a-larger-maps.md) (chunked terrain in-memory/streaming layout). See the
  [scope Decision](open-questions.md).
- H3 reserves a terrain section for [B4a-5's chunked heightmap representation](deep-dives/b4a-larger-maps.md)
  rather than inventing a second map-format effort; whichever of H3 or B4a-5 lands first defines
  the section boundary, and the other adopts it.
- H2 and H2a mirror [C1's format-agnostic import-abstraction pattern](track-c-external-assets.md)
  (interchange types plus a swappable-format seam, reference implementation being today's parser
  refactored), applied to map and object-definition data instead of models and animations.
- H4 and H6 feed [Track D](track-d-gameplay-expansion.md)'s D3 external content ingestion and the
  standalone [ModCollector tool](plans/mod-collector.md): content staged by ModCollector and
  layered through `AssetStore` scopes can adopt the new object-definition format once it exists.
- H4 coordinates with [F4a](track-f-terrain-destruction.md). The schema stores stable engine
  concepts such as participation override, collision layer/mask, material, and shape source, never
  `BepuPhysics` classes, handles, broadphase state, or package-version-specific data.
- H2b converts the engine to metric after H2a merges and supersedes the presentation-only [metric-units plan](../plans/metric-units.md). It owns conversion at the C4 glTF, C4a NIF, and H2a materialization boundaries; those phases keep their axis conventions.
- H2a depends on P9 and C4d; the storage foundation retains the Track P (P7) gate. F4a coordination
  affects optional physics fields, not the format seam's execution order.
