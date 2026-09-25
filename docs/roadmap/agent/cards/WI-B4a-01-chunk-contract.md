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

1. Map the 128-interval chunk to current terrain patch sizes and the ten-unit lattice; note border and apron interactions.
2. Estimate resident memory per chunk (heights, blend, passability, GPU patch) and whole-front totals at 1 m per game unit.
3. List engine call sites that assume a whole-map resident grid.
4. Add a `Chunk contract (proposed)` section to `b4a-larger-maps.md` with the layout table link, estimates, and call-site list; flag open items as Decisions for the operator.

## Files likely to change

- `docs/roadmap/deep-dives/b4a-larger-maps.md`

## Acceptance criteria

- [ ] The section cites file:line for every call site listed.
- [ ] Memory estimates cover a Somme-scale and whole-front map.
- [ ] Standard validation passes.

## Validation commands

```sh
echo '{"args":["--no-sandbox"]}' > /tmp/pp.json
npx -y @mermaid-js/mermaid-cli@latest -p /tmp/pp.json -i docs/roadmap/deep-dives/b4a-larger-maps.md -o /tmp/mm/card.svg
```

## Out of scope / guardrails

No code changes; no B4a-5 implementation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
