# Track H: Map & Unit Storage Formats

> Part of the [Large-Scale RTS Roadmap](README.md). A new on-disk storage system for maps and
> unit/object template data: a format-agnostic interchange abstraction, a versioned successor to
> the `.map` format, a versioned successor to per-game INI object-definition storage, and the
> migration/tooling to move existing content onto it. This is a persistence and schema-design
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
| H2. Format-agnostic interchange abstraction | Define engine-internal interchange types for map data (terrain, blend/texture tiles, placed objects) and object/unit template data, plus a format seam (an `IMapFormat` / `IObjectTemplateFormat`-style abstraction) behind the existing `AssetStore` / `OnDemandAssetLoadStrategy` loading path, mirroring the [C1 import-abstraction pattern](track-c-external-assets.md). Refactor the existing `.map` reader and the `IniParser`/`ObjectDefinition` path to be the reference implementation behind this seam (proves the seam; no new format yet). | The legacy `.map` reader and INI object-definition parser load entirely as one implementation of the new format seam, with no behavior change. | Not started | H1 |
| H3. New map storage format | Design and implement a new on-disk map format behind the H2 seam: versioned, schema-evolvable, and structured so a terrain payload section can host [B4a-5's chunked heightmap representation](deep-dives/b4a-larger-maps.md) without a second, competing map-format effort. Scope here is the container, schema, and versioning; B4a-5 owns the chunked terrain data model and streaming behavior that fills the terrain section. | A map authored in the new format round-trips (write, reload) with terrain, blend, and placed-object data intact, and reserves a terrain section B4a-5 can adopt for chunked storage. | Not started | H2 |
| H4. New object/unit-definition storage format | Design and implement a new storage format behind the H2 seam for object/unit template data (`ObjectDefinition` plus closely-coupled weapons, upgrades, locomotors, and F4a engine-level physics participation/material/filter overrides), with explicit schema versioning and forward-compatible field addition. Keep INI as a permanent legacy read path and keep Bepu types/handles out of the schema. | A unit authored in the new format loads with full parity to an equivalent INI-authored unit, including resolved physics metadata, and adding a new field does not break older content. | Not started | H2 |
| H5. Legacy migration & compatibility tooling | Build conversion tooling that reads legacy `.map` and INI content and emits the new H3/H4 formats, plus a round-trip validator that confirms converted content is behaviorally equivalent to the original. | A converted legacy map and unit roster load through the new formats and play identically (per the P0 assertion layer) to loading the original `.map`/INI content. | Not started | H3, H4 |
| H6. Authoring & pipeline integration | Wire WorldBuilder (map authoring) and the modding/content pipeline (ModCollector staging, `AssetStore` scope layering used by [Track D](track-d-gameplay-expansion.md)) to read and write the new formats, preserving today's fast INI edit/hot-reload iteration loop for object definitions during development. | WorldBuilder authors, saves, and reloads a map in the new format, and a ModCollector-staged content pack's object definitions load through the new format alongside built-in content. | Not started | H5 |
| H7. Validation & round-trip regression | Add automated round-trip regression coverage: load legacy, convert, reload, compare for a representative map and unit roster, plus a ReplaySketch-authored scenario that builds and fights using new-format content, asserted headlessly per the P0 harness pattern. | A regression suite catches a round-trip mismatch for maps or object definitions, and a ReplaySketch scenario using new-format content passes the same headless assertions as its legacy-format equivalent. | Not started | H5, H6 |

## Phase resources

A navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams are required before any phase's first task card is marked `Ready`; all H-phase deep
dives are yet to be written.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| H1 | Written by `WI-H1-01` | — | [`WI-H1-01`](agent/cards/WI-H1-01-map-format-audit.md), [`WI-H1-02`](agent/cards/WI-H1-02-format-decision-options.md) |
| H2 | Extended by `WI-H2-01` | — | [`WI-H2-01`](agent/cards/WI-H2-01-map-interchange-design.md) |
| H3 | — | — | — |
| H4 | — | — | — |
| H5 | — | — | — |
| H6 | — | — | — |
| H7 | — | — | — |

## Cross-track dependencies

Hard dependencies are in the phase table above; these are the coordination notes:

- Track H is a persistence and schema-design concern: it designs on-disk format, versioning, and
  tooling. It does not re-architect runtime storage, which stays owned by
  [B3](track-b-scale.md) (unit/`GameObject` in-memory layout) and
  [B4a-5](deep-dives/b4a-larger-maps.md) (chunked terrain in-memory/streaming layout). See the
  [scope Decision](open-questions.md).
- H3 reserves a terrain section for [B4a-5's chunked heightmap representation](deep-dives/b4a-larger-maps.md)
  rather than inventing a second map-format effort; whichever of H3 or B4a-5 lands first defines
  the section boundary, and the other adopts it.
- H2 mirrors [C1's format-agnostic import-abstraction pattern](track-c-external-assets.md)
  (interchange types plus a swappable-format seam, reference implementation being today's parser
  refactored), applied to map and object-definition data instead of models and animations.
- H4 and H6 feed [Track D](track-d-gameplay-expansion.md)'s D3 external content ingestion and the
  standalone [ModCollector tool](plans/mod-collector.md): content staged by ModCollector and
  layered through `AssetStore` scopes can adopt the new object-definition format once it exists.
- H4 coordinates with [F4a](track-f-terrain-destruction.md). The schema stores stable engine
  concepts such as participation override, collision layer/mask, material, and shape source, never
  `BepuPhysics` classes, handles, broadphase state, or package-version-specific data.
- Track H's hard dependency remains Track P (P7); F4a coordination affects optional physics fields,
  not the format seam's execution order.
