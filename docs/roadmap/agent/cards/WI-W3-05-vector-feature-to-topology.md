# Task Card `WI-W3-05`: Map generated features to topology

| Field | Value |
|---|---|
| Card ID | `WI-W3-05` |
| Roadmap phase | [Track W · W3](../../track-w-western-front.md): geospatial sector |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3-03`, `WI-B4b-16` |
| Diagrams | [W3 §6](../../deep-dives/w3-geospatial-sector.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3-03` and `WI-B4b-16` are Done and `GeneratedFeature` exists.

Map B4b generated roads, rail, rivers, villages, woods, and front lines into W topology records.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3-geospatial-sector.md` §3.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.3.

## Steps

1. Add feature-layer classification for the W3 required layers.
2. Attach feature references to regions and cells intersecting villages, woods, rivers, and front lines.
3. Create movement/logistics node and link candidates from roads and rail.
4. Test missing and duplicate feature ids.

## Files likely to change

- Western Front feature mapping files
- Western Front tests

## Acceptance criteria

- [ ] Every generated node and link has a source feature id.
- [ ] Front-line features split rear/front bands deterministically.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontFeatureMapping"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No historical accuracy validation.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
