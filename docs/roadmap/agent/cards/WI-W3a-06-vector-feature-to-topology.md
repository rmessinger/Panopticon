# Task Card `WI-W3a-06`: Map generated features to topology

| Field | Value |
|---|---|
| Card ID | `WI-W3a-06` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-04`, `WI-B4b-16` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-04` and `WI-B4b-16` are Done and `GeneratedFeature` exists.

Map B4b generated features into W topology records per the W3a §4 layer table.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §4.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.3.

## Steps

1. Add feature-layer classification for roads, rail, rivers, villages, woods, and the optional band
   layer (W3b front lines), plus the W3a `bridge`, `pontoon`, `fortress`, `inundation`, and
   `border` layers.
2. Attach feature references to regions and cells intersecting villages, woods, rivers, and band
   lines.
3. Create movement/logistics node and link candidates from roads and rail, and river-crossing links
   from `bridge` and `pontoon` features.
4. Apply cover classes (`village`, `woods`, `fortress`) and inundation flags to cells; mark cells
   beyond `border` as neutral.
5. Test missing and duplicate feature ids.

## Files likely to change

- Western Front feature mapping files
- Western Front tests

## Acceptance criteria

- [ ] Every generated node and link has a source feature id.
- [ ] A band layer, when present, splits rear/front bands deterministically.
- [ ] Crossing links, the fortress node, cover classes, inundation flags, and neutral cells match
      the §4 layer table on a synthetic payload.
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
