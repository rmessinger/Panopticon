# Task Card `WI-W3-01`: Document operator-local Somme geospatial inputs

| Field | Value |
|---|---|
| Card ID | `WI-W3-01` |
| Roadmap phase | [Track W · W3](../../track-w-western-front.md): geospatial sector |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | W2 and `WI-B4b-18` |
| Diagrams | [W3 §6](../../deep-dives/w3-geospatial-sector.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once W2 and `WI-B4b-18` are Done and the named types are reconciled.

Create the operator-local input checklist and manifest example for the Somme sector without committing source data.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3-geospatial-sector.md` §1-§2.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §2, §4, and §6.1.

## Steps

1. Write an operator note or manifest fixture template under the implementation workspace, not under source-data folders.
2. Include extent, EPSG:32631, `metersPerGameUnit` 0.3048, vertical datum `NGF-IGN69`, and required vector layers.
3. Record the `gdalwarp` source and target CRS expectation as operator-local instructions.
4. State that no generated payload or raw source data is committed.

## Files likely to change

- Operator-local manifest/template path
- Optional implementation note

## Acceptance criteria

- [ ] Template names every required source and metadata field.
- [ ] Reprojection instructions identify Lambert-93 to UTM 31N and `NGF-IGN69` metadata.
- [ ] No source data path is committed except placeholders.

## Validation commands

```sh
python docs/roadmap/scripts/check_links.py docs/roadmap/deep-dives/w3-geospatial-sector.md || true
```

## Out of scope / guardrails

No data acquisition or generated payload commit.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
