# Task Card `WI-B4a-01`: Ground the B4a-5 chunk contract

| Field | Value |
|---|---|
| Card ID | `WI-B4a-01` |
| Roadmap phase | [Track B · B4a](../../track-b-scale.md): B4a-5 chunked terrain |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [B4a diagrams](../../deep-dives/b4a-larger-maps.md#architecture-diagrams) |

## Goal

Ground the B4b §5.1a chunk layout against the engine and record it as the proposed B4a-5 runtime chunk contract.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.1a.
- `docs/roadmap/deep-dives/b4a-larger-maps.md`.
- `src/Panopticon.Simulation/Terrain/HeightMap.cs`, `Terrain.cs`, `TerrainPatch*.cs`.

## Steps

1. Map the 128-interval chunk to current terrain patch sizes and the ten-unit lattice.
2. Note border and apron interactions.
3. Estimate resident memory per chunk and Somme-scale totals.
4. List engine call sites that assume a whole resident grid.
5. Update the B4a chunk contract section if the source audit changes it.

## Files likely to change

- `docs/roadmap/deep-dives/b4a-larger-maps.md`

## Acceptance criteria

- [ ] The section cites file:line for every call site listed.
- [ ] Memory estimates cover a Somme-scale map.
- [ ] The diagram renders.

## Validation commands

```sh
mkdir -p docs/roadmap/.validation-b1h
echo '{"args":["--no-sandbox"]}' > docs/roadmap/.validation-b1h/pp.json
npx -y @mermaid-js/mermaid-cli@latest -p docs/roadmap/.validation-b1h/pp.json -i docs/roadmap/deep-dives/b4a-larger-maps.md -o docs/roadmap/.validation-b1h/card.svg
```

## Out of scope / guardrails

No code changes and no H3 serialization.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
