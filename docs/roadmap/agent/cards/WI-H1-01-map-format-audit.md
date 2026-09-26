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

1. Create the deep dive with an Audience line and sections for scope, current format, readers and writers, limits, and consumers.
2. List every `MapFile` section with version handling and file:line references.
3. Add a mermaid class diagram of current `MapFile` section types.
4. Link the deep dive from Track H resources if the coordinator permits editing that file.

## Files likely to change

- New `docs/roadmap/deep-dives/h1-h2-map-storage.md`
- `docs/roadmap/track-h-storage-formats.md` if ownership allows

## Acceptance criteria

- [ ] Every `MapFile` section is listed with file:line references.
- [ ] The diagram renders.
- [ ] The audit does not make a format decision.

## Validation commands

```sh
mkdir -p docs/roadmap/.validation-b1h
echo '{"args":["--no-sandbox"]}' > docs/roadmap/.validation-b1h/pp.json
npx -y @mermaid-js/mermaid-cli@latest -p docs/roadmap/.validation-b1h/pp.json -i docs/roadmap/deep-dives/h1-h2-map-storage.md -o docs/roadmap/.validation-b1h/card.svg
```

## Out of scope / guardrails

No format decision and no implementation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
