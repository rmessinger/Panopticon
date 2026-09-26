# Task Card `WI-H2-01`: Design map interchange for the settled package format

| Field | Value |
|---|---|
| Card ID | `WI-H2-01` |
| Roadmap phase | [Track H · H2](../../track-h-storage-formats.md): interchange abstraction |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-H1-02` |
| Diagrams | none |

## Goal

Design H2 format-neutral map interchange types for B4b-7, B4a-5, and H3 with the settled directory package and raw chunk-layer encoding.

## Context to read (and only this)

- `docs/roadmap/deep-dives/h1-h2-map-storage.md`.
- `docs/roadmap/deep-dives/b4a-larger-maps.md` chunk contract.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.1a and §6.1.
- `docs/roadmap/agent/cards/WI-H1-02-format-decision-options.md`.

## Steps

1. Add an interchange section for chunks, material layers, masks, placed objects, waypoints, sides, provenance, and transform metadata.
2. Ensure every B4b §6.1 payload field has an H2 interchange home.
3. Add a mermaid class diagram of producers, interchange types, and consumers.
4. Link provisional H3 cards as package-format follow-up.

## Files likely to change

- `docs/roadmap/deep-dives/h1-h2-map-storage.md`
- `docs/roadmap/agent/cards/`

## Acceptance criteria

- [ ] Every B4b §6.1 payload field has an interchange home.
- [ ] The settled H3 directory package is the only target container.
- [ ] New implementation cards are concrete and linked from the deep dive.

## Validation commands

```sh
mkdir -p docs/roadmap/.validation-b1h
echo '{"args":["--no-sandbox"]}' > docs/roadmap/.validation-b1h/pp.json
npx -y @mermaid-js/mermaid-cli@latest -p docs/roadmap/.validation-b1h/pp.json -i docs/roadmap/deep-dives/h1-h2-map-storage.md -o docs/roadmap/.validation-b1h/card.svg
```

## Out of scope / guardrails

No H3 writer code and no object-template format design beyond map references.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
