# Task Card `WI-R2-03`: Building footprints as terrain masks

| Field | Value |
|---|---|
| Card ID | `WI-R2-03` |
| Roadmap phase | [Track R · R2](../../track-r-real-world-terrain.md): real-world structures as impassable regions |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-R2-02` (provider source profiles) |
| Diagrams | [§10.4 standalone import and dev preview flow](../../deep-dives/r-real-world-terrain.md#104-standalone-import-and-dev-preview-flow) |

## Goal

Give real-world buildings gameplay meaning without placing objects: the preparation script adds building footprint polygons to the area KML, and an example mapping rule turns them into impassable and unbuildable regions. Units in the Sedan preview then path around the old town's buildings instead of through them. Elevation stays bare-earth and imagery stays flat; see deep dive §5.4.

## Context to read (and only this)

- `docs/roadmap/deep-dives/r-real-world-terrain.md` §5.3 (vector features), §5.4 (structures, added with this card), §8.1.
- `docs/roadmap/agent/cards/WI-B4b-16-feature-mapping.md`, `WI-B4b-17-terrain-masks.md`, `WI-B4b-23-payload-to-mapfile.md` (mask to `BlendTileData` passability).
- `docs/roadmap/agent/cards/WI-R2-02-provider-source-profiles.md` and `scripts/geo/prepare_area.py`.

## Steps

1. Add an optional `footprints` profile field: WFS URL, layer name, kept attributes (source ID, use or nature, height when present), and licence and attribution text. Fill it in `providers/ign-fr.json` with the IGN BD TOPO building layer from the Géoplateforme WFS (layer name found with `ogrinfo` and recorded in `sources.txt`). `copernicus-glo30` has none.
2. In `prepare_area.py`, when the profile has footprints and `--no-footprints` is not given:
   - fetch footprints intersecting the extent with `ogr2ogr -spat` in EPSG:4326;
   - drop footprints smaller than `--min-footprint-m2` (default 20), measured in the target UTM zone;
   - write them into the same area KML as a `buildings` folder, so the importer still receives one KML (§3);
   - record footprint count, dropped count, and the BD TOPO edition in `sources.txt`.
3. Add `test/Panopticon.Geospatial.Tests/Examples/building-footprints.json`, a manifest fragment with one rule: `layerName` `buildings`, polygon, target Passability (impassable) and Buildability (unbuildable). Add the same rule to `sedan-ign-proof.json`.
4. Add `test/Panopticon.Geospatial.Tests/Generation/BuildingFootprintMaskTests.cs`: a synthetic KML with a 30 m square footprint and a 2 m square one, run through `FeatureMapper` and `TerrainMaskGenerator` at the W3a scale; assert the large square's interior cells are `Impassable | Unbuildable`, cells outside are clear, and the report counts the small one as a footprint that set no cells.
5. If step 4 shows the mask report has no per-feature zero-cell count, add it (one counter and report field) with its own commit.
6. Extend `scripts/tests/test_prepare_area.py` for the footprint command line, the area filter, and `--no-footprints`.
7. Operator run: re-prepare and re-import the Sedan proof area. Record footprint count, masked cell count and share, zero-cell footprints, and a screenshot of the preview with the mask overlaid on the old town. Order a group of units across the old town and capture a GIF showing them follow streets.

## Files likely to change

- `scripts/geo/prepare_area.py`, `scripts/geo/providers/ign-fr.json`
- `scripts/tests/test_prepare_area.py`
- `test/Panopticon.Geospatial.Tests/Examples/building-footprints.json` (new), `sedan-ign-proof.json`
- `test/Panopticon.Geospatial.Tests/Generation/BuildingFootprintMaskTests.cs` (new)
- `src/Panopticon.Geospatial/Generation/TerrainMask*.cs` (only for step 5)
- `docs/roadmap/deep-dives/r-real-world-terrain.md` §8.1 (results)

## Acceptance criteria

- [ ] `prepare_area.py --provider ign-fr` writes building polygons into the area KML and records counts and edition in `sources.txt`.
- [ ] The synthetic mask test passes and reports footprints that set no cells.
- [ ] The Sedan preview marks the old town's buildings impassable, and the recorded GIF shows units following streets.
- [ ] No IGN-derived file is committed.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Geospatial.Tests --filter "FullyQualifiedName~BuildingFootprint|FullyQualifiedName~Manifest"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
python3 -m unittest discover -s scripts/tests -p "test_prepare_area.py"
```

## Out of scope / guardrails

- Placing structure objects, building heights, and bridges (deferred to R3, see §5.4).
- OpenStreetMap footprints (ODbL share-alike needs its own licence decision).
- Changing the bare-earth elevation or flattening imagery under footprints.
- New manifest target kinds; the existing Passability and Buildability regions suffice.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick the R2 building footprints item in the checklist and update the README Track R progress rows.
- Append a result line to `.exchange`.
