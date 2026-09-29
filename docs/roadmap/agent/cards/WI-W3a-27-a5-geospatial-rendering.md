# Task Card `WI-W3a-27`: Render geospatial topology in A5

| Field | Value |
|---|---|
| Card ID | `WI-W3a-27` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-04`, `WI-A5-18` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-04` and `WI-A5-18` are Done.

Project generated Western Front regions, cells, front line, nodes, and links into the A5 result view.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §4 and §12.
- `docs/roadmap/deep-dives/a5-visualization.md` §4.

## Steps

1. Extend the domain visualization projection for W topology source references.
2. Render region and cell boundaries, front lines, nodes, and links in the 2-D overview.
3. Add hit-test details for source ids.
4. Add a focused rendering test or screenshot validation.

## Files likely to change

- A5 visualization projection files
- ReplaySketch/A5 tests

## Acceptance criteria

- [ ] Selected topology items show stable W keys and source ids.
- [ ] Rendering reads recorded state, not a rerun.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontGeospatialRender"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No full 3-D geospatial terrain rendering.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
