# Task Card `WI-W3a-02`: Document Sedan geospatial inputs

| Field | Value |
|---|---|
| Card ID | `WI-W3a-02` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | W2 and `WI-B4b-18` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once W2 and `WI-B4b-18` are Done and the named payload types are reconciled.

Create the operator-local Sedan input checklist and manifest example without committing source data.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §1, §3, and §4.
- `docs/roadmap/deep-dives/w3b-somme-sector.md` §1-§2 for the Somme manifest pattern.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §2, §4, and §6.1.

## Steps

1. Write an operator manifest template under the implementation workspace, not under source-data folders.
2. Include extent 49.58-49.82 N and 4.72-5.20 E, EPSG:32631, `metersPerGameUnit` 0.3048, and vertical datum `NGF-IGN69`.
3. Name RGE ALTI 1 m in Lambert-93 and Wallonia 1 m DTM in Belgian Lambert as operator-local DEM inputs.
4. Record the `gdalwarp` mosaic step and the TAW-to-NGF-IGN69 constant offset field.
5. Name OSM-derived modern layers and operator-digitized 1870 layers: rail, `bridge`, `pontoon`, `fortress`, `inundation`, and `border`.

## Files likely to change

- Operator-local Sedan manifest or implementation note

## Acceptance criteria

- [ ] Manifest names DEM, vector, and digitized 1870 sources, CRS, datum offset, and non-committed paths.
- [ ] Public-domain staff maps are named as the source for operator-digitized 1870 layers.
- [ ] No IGN, NGI, OSM extract, digitized vector, generated payload, or reprojected raster is committed.

## Validation commands

```sh
python docs/roadmap/scripts/check_links.py docs/roadmap/deep-dives/w3a-sedan-1870.md || true
```

## Out of scope / guardrails

No data acquisition, source-data commit, or generated payload commit.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
