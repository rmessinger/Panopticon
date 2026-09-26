# Task Card `WI-W3-04`: Derive 1000 m regions and 125 m cells

| Field | Value |
|---|---|
| Card ID | `WI-W3-04` |
| Roadmap phase | [Track W · W3](../../track-w-western-front.md): geospatial sector |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3-03`, `WI-B4b-06` |
| Diagrams | [W3 §6](../../deep-dives/w3-geospatial-sector.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3-03` and `WI-B4b-06` are Done and `LocalMapGrid` exists.

Implement the W3 region and cell grid over the B4b projected extent.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3-geospatial-sector.md` §1 and §3.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.1a.

## Steps

1. Use projected meters from `LocalMapGrid`.
2. Create 1000 m regions clipped to the payload extent.
3. Create 8 x 8 cells per region, 125 m per cell.
4. Expose projected and game-unit bounds for each record.
5. Test expected sizes and key coverage.

## Files likely to change

- Western Front topology grid files
- Western Front tests

## Acceptance criteria

- [ ] Region and cell dimensions match 1000 m and 125 m within projection tolerance.
- [ ] Keys cover the playable extent without duplicate cells.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontRegionGrid"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No vector feature mapping.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
