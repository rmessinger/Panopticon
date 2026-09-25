# Task Card `WI-H2-01`: Map interchange type design

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

Design the H2 format-neutral map interchange types that B4b-7 and H3 target, after the operator resolves the WI-H1-02 Decisions.

## Context to read (and only this)

- `docs/roadmap/deep-dives/h1-h2-map-storage.md`.
- Resolved H1 Decisions in `docs/roadmap/open-questions.md`.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §6 and §10.1.

## Steps

1. Add an interchange section: terrain chunk source, material layers, masks, placed objects, waypoints, sides, provenance, and the transform metadata B4b retains.
2. Add a mermaid class diagram of the interchange types and their producers (legacy reader, B4b) and consumers (H3 writer, engine loader, WorldBuilder).
3. Decompose H2 implementation into Ready cards using the task-card template.

## Files likely to change

- `docs/roadmap/deep-dives/h1-h2-map-storage.md`
- `docs/roadmap/agent/cards/`

## Acceptance criteria

- [ ] Every B4b §6.1 payload field has an interchange home.
- [ ] New H2 cards exist and are linked from the deep dive.
- [ ] Standard validation passes.

## Validation commands

```sh
echo '{"args":["--no-sandbox"]}' > /tmp/pp.json
npx -y @mermaid-js/mermaid-cli@latest -p /tmp/pp.json -i docs/roadmap/deep-dives/h1-h2-map-storage.md -o /tmp/mm/card.svg
```

## Out of scope / guardrails

No code.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
