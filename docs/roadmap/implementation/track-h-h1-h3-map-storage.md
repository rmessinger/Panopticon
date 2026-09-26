# Implementation plan: Track H · H1-H3 map storage

> Audience: human + agent implementation plan for map storage audit, interchange, and H3 package work.

Source of truth: [Track H](../track-h-storage-formats.md), the H1/H2 map-storage deep dive created
by `WI-H1-01`, [B4a larger maps](../deep-dives/b4a-larger-maps.md), and
[B4b geospatial map generation](../deep-dives/b4b-geospatial-map-generation.md).

## Settled format choices

- Container: directory package.
- Manifest: `manifest.json` at package root.
- Chunk storage: one file per chunk per layer, mirroring B4b §6.1 payload shape.
- Layer encoding: raw little-endian arrays with JSON metadata.
- Runtime owner: B4a owns chunk residency and paging.
- On-disk owner: H3 owns manifest, schema, versioning, and chunk bytes.

## Card sequence

| Card | Sub-phase | Output | Status | Prerequisites |
|---|---|---|---|---|
| [`WI-H1-01`](../agent/cards/WI-H1-01-map-format-audit.md) | H1 | Legacy map audit deep dive | Ready | none |
| [`WI-H1-02`](../agent/cards/WI-H1-02-format-decision-options.md) | H1 | Confirm settled format choices against audit | Ready | `WI-H1-01` |
| [`WI-H2-01`](../agent/cards/WI-H2-01-map-interchange-design.md) | H2 | H2 map interchange design | Ready | `WI-H1-02` |
| [`WI-H3-01`](../agent/cards/WI-H3-01-map-package-manifest-schema.md) | H3 | Manifest schema | Draft | `WI-H2-01` |
| [`WI-H3-02`](../agent/cards/WI-H3-02-chunk-layer-reader-writer.md) | H3 | Chunk layer reader and writer | Draft | `WI-H2-01`, `WI-H3-01` |
| [`WI-H3-03`](../agent/cards/WI-H3-03-legacy-to-package-importer-hooks.md) | H3 | Legacy `MapFile` to package hooks | Draft | `WI-H2-01`, `WI-H3-01`, `WI-H3-02` |
| [`WI-H3-04`](../agent/cards/WI-H3-04-map-package-roundtrip-tests.md) | H3 | Round-trip tests | Draft | `WI-H3-01`, `WI-H3-02`, `WI-H3-03` |

## Acceptance by sub-item

| Sub-item | Acceptance criteria |
|---|---|
| H1 | The audit lists legacy `.map` limitations with file:line references and confirms no contradiction with settled H3 choices. |
| H2 | Every B4b §6.1 payload value has a format-neutral interchange home. |
| H3-1 | `manifest.json` names versions, chunk layout, layers, metadata, provenance, and hashes. |
| H3-2 | Chunk layer files read and write little-endian arrays with declared dimensions and hashes. |
| H3-3 | Legacy resident `MapFile` values can populate the H2 interchange and write an H3 package. |
| H3-4 | Terrain, layers, masks, placed objects, metadata, and provenance round-trip semantically. |

## Coordination notes

- B4a-5 consumes H3 contracts for package I/O but owns runtime residency.
- B4b-7 consumes H2 and H3 to write generated geospatial packages.
- H6 later wires WorldBuilder read and write lifecycle to H3.
