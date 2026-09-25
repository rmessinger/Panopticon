# Task Card `WI-H1-01`: Audit legacy map storage

| Field | Value |
|---|---|
| Card ID | `WI-H1-01` |
| Roadmap phase | [Track H · H1](../../track-h-storage-formats.md): format audit |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | none |

## Goal

Create `deep-dives/h1-h2-map-storage.md` with a grounded audit of the legacy `.map` format and its readers and writers.

## Context to read (and only this)

- `docs/roadmap/track-h-storage-formats.md` H1-H3.
- `src/Panopticon.Simulation/Data/Map/`.
- `src/Panopticon.Tools.WorldBuilder.Core/EditorMap.cs`, `NewMapFactory.cs`.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §6.

## Steps

1. Create the deep dive with an Audience line and sections: scope, current format (chunk table: name, version handling, sizes, resident cost), readers/writers, limits (C1-C6 from B4a), consumers (engine, WorldBuilder, B4b preview).
2. Add a mermaid class diagram of the current `MapFile` section types.
3. Link it from the Track H resources row.

## Files likely to change

- New `docs/roadmap/deep-dives/h1-h2-map-storage.md`
- `docs/roadmap/track-h-storage-formats.md`

## Acceptance criteria

- [ ] Every `MapFile` section is listed with file:line references.
- [ ] The diagram renders.
- [ ] Standard validation passes.

## Validation commands

```sh
echo '{"args":["--no-sandbox"]}' > /tmp/pp.json
npx -y @mermaid-js/mermaid-cli@latest -p /tmp/pp.json -i docs/roadmap/deep-dives/h1-h2-map-storage.md -o /tmp/mm/card.svg
```

## Out of scope / guardrails

No format decision.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-H1-02`.
