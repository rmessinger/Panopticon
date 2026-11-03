# Task Card `WI-R2-01`: Sedan real-world preview proof

| Field | Value |
|---|---|
| Card ID | `WI-R2-01` |
| Roadmap phase | [Track R · R2](../../track-r-real-world-terrain.md): early operator proof on the dev preview |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `M` |
| Prerequisites | `WI-B4b-24` (which needs `WI-B4b-19`, `WI-B4b-20`, `WI-B4b-23`) |
| Diagrams | [§10.4 standalone import and dev preview flow](../../deep-dives/r-real-world-terrain.md#104-standalone-import-and-dev-preview-flow) |

## Goal

Import one small real area of Sedan (Ardennes, France) from IGN data and a generated KML extent, pass independent control points, and play the generated preview map in Generals. This is the first real-data run of the R1/R2 pipeline, ahead of the R5 package and WorldBuilder proof.

## Context to read (and only this)

- `docs/roadmap/deep-dives/r-real-world-terrain.md` §2 (source boundary), §4 (coordinates, datum, tolerances), §6.1 (payload and preview), §8 (validation layers, including §8.1 added with this card).
- `test/Panopticon.Geospatial.Tests/Examples/worldcover-materials.json` (manifest shape).
- `src/Panopticon.Tools.GeoImport/` and the `WI-B4b-24` game-backed preview test.
- `docs/screenshots.md`.

## Area and sources

| Item | Choice |
|---|---|
| Area | About 1.1 km square centred on the Château fort de Sedan (49.702 N, 4.947 E), including the old town and the Meuse; inside the W3a Sedan extent |
| Extent and control-point placemarks | Polygon `sedan-proof-extent` (a 1100 m square in EPSG:32631 around the centre) and named benchmark placemarks, generated as KML (EPSG:4326) by the preparation script |
| Elevation | IGN RGE ALTI 1 m, department 08 (Licence Ouverte 2.0), Lambert-93, datum `NGF-IGN69`; stated vertical accuracy from the tile quality metadata |
| Imagery | IGN BD ORTHO 20 cm, department 08 (Licence Ouverte 2.0), Lambert-93 |
| Vertical control | 3 to 5 IGN levelling benchmarks (repères de nivellement) inside the extent, altitudes from their IGN geodesy fiches |
| Target | `utm` zone 31 north (EPSG:32631), `metersPerGameUnit` 0.3048 (W3a scale, 3.048 m lattice), vertical exaggeration 1 |

Google basemap imagery and terrain are not used (see §2). No source file, derived raster, payload, or `.map` is committed.

The whole run is command-line only: `curl`, `7z`, and GDAL 3.x command-line tools (`gdaltransform`, `ogr2ogr`, `ogrinfo`, `gdalbuildvrt`, `gdalwarp`) with the JP2OpenJPEG driver for BD ORTHO. No desktop application is required. Google Earth Pro may open the generated KML for a visual check; it is not a step.

## Steps

1. Add `scripts/geo/prepare-sedan-proof.sh <work dir>`, an idempotent operator script (never run in CI) that writes everything under the work dir:
   - Centre: `gdaltransform -s_srs EPSG:4326 -t_srs EPSG:32631` on `4.947 49.702`; extent = centre ± 550 m in EPSG:32631; warp window = extent + 50 m.
   - Downloads: list the IGN Géoplateforme download service (`https://data.geopf.fr/telechargement/`) resources for RGE ALTI 1 m and BD ORTHO 20 cm, select the department 08 (`D008`) archives, fetch with `curl -fL -C -` (resumable), and extract with `7z x`. Record archive names and edition dates in a `sources.txt` beside the data.
   - Elevation: `gdalbuildvrt -a_srs EPSG:2154` over the RGE ALTI tiles, then `gdalwarp -t_srs EPSG:32631 -tr 1 1 -r bilinear -te <warp window> -ot Float32 -co COMPRESS=NONE` to a classic single-image GeoTIFF.
   - Imagery: `gdalbuildvrt` over the BD ORTHO JP2 tiles, then `gdalwarp -t_srs EPSG:32631 -tr 1 1 -r average -te <warp window> -ot Byte -b 1 -b 2 -b 3 -co COMPRESS=NONE`.
   - Benchmarks: query the Géoplateforme WFS (`WFS:https://data.geopf.fr/wfs/ows`) levelling-benchmark layer (found with `ogrinfo` and recorded in `sources.txt`) inside the extent with `ogr2ogr -spat`; keep 3 to 5 benchmarks with published NGF-IGN69 altitudes.
   - KML: write the extent polygon (`sedan-proof-extent`, transformed back to EPSG:4326) and one placemark per benchmark into `sedan-proof.kml` with `ogr2ogr -f KML`.
   - Print the benchmark names, lat/lon, and altitudes for the manifest's `controlPoints`.
2. Add `test/Panopticon.Geospatial.Tests/Examples/sedan-ign-proof.json`: sources with relative paths, labels, and attribution (`IGN, Licence Ouverte 2.0`; `generated extent KML`), DEM `verticalDatum` `NGF-IGN69` with its stated `verticalAccuracyMeters`, `vertical.originMeters` near the extent minimum, the target and scale above, `raster.noData` `nearest-valid`, and `controlPoints` (added by `WI-B4b-20`):
   - each benchmark with lat, lon, and expected elevation from the WFS record; vertical tolerance = DEM stated accuracy + 1 m for mark height above ground, recorded per point;
   - two imagery points with expected RGB and per-channel tolerance: Meuse water and one large bright roof or square;
   - horizontal game XY left unevaluated unless an independent coordinate source is supplied (§6.1).
3. Add a manifest test that loads `sedan-ign-proof.json` with `ManifestLoader` and `ManifestValidator` without source files present and asserts no schema errors, so the example cannot rot.
4. Run `GeoImport import --manifest sedan-ign-proof.json --out <payload>` and `GeoImport report --payload <payload>`. Expected: 3 x 3 chunks, bilinear or area-weighted kernels recorded, no unfilled no-data, all control points pass.
5. Run `GeoImport preview --payload <payload> --textures <map.json> --out <maps dir>`, then an Interactive test `SedanOperatorProofTests` that reads `PANOPTICON_GEO_PROOF_DIR`, starts a skirmish on the generated map the same way the `WI-B4b-24` test does, runs 300 logic frames, and asserts terrain height at each benchmark within tolerance after restoring the preview Z offset.
6. Capture an overview and a close-up of the castle promontory and Meuse bank under versioned names in the screenshot viewer; for imagery alignment, compare against a matching BD ORTHO crop.
7. Record results in deep dive §8.1: data versions and tile names from `sources.txt`, the script revision, per-point residuals, kernels and sample-distance ratios, fill counts, quantization loss, import wall time, and screenshot links.

## Files likely to change

- `scripts/geo/prepare-sedan-proof.sh` (new)
- `test/Panopticon.Geospatial.Tests/Examples/sedan-ign-proof.json` (new)
- `test/Panopticon.Geospatial.Tests/Manifest/` (example manifest test)
- `test/Panopticon.Simulation.Tests/` preview test folder (`SedanOperatorProofTests`, `Category=Interactive`)
- `docs/roadmap/deep-dives/r-real-world-terrain.md` §8.1 (results)

## Acceptance criteria

- [ ] `prepare-sedan-proof.sh` runs unattended from an empty work dir to the DEM, imagery, and KML, and a second run reuses existing downloads.
- [ ] The example manifest validates in CI without source data.
- [ ] Operator run: every benchmark passes its vertical tolerance, both imagery points pass, and the report records `NGF-IGN69` and the kernels.
- [ ] The preview map loads in Generals and runs 300 logic frames with benchmark heights within tolerance.
- [ ] Screenshots show the castle promontory and the Meuse in the right place against the BD ORTHO crop.
- [ ] §8.1 holds the recorded results; no IGN or Google-derived file is committed.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Geospatial.Tests --filter "FullyQualifiedName~Manifest"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
PANOPTICON_GEO_PROOF_DIR="<operator data dir>" CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "FullyQualifiedName~SedanOperatorProof"
```

## Out of scope / guardrails

- Any importer change beyond fixing a defect this run exposes; such fixes get their own commit with a synthetic regression test.
- Wallonia DTM, a cross-border extent, the full W3a extent, and 1870 vector digitizing (W3a).
- H3 package, WorldBuilder round-trip, and B1 measurements (R3 to R5).
- Google basemap imagery or terrain, and any network access from the importer.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick the R2 Sedan proof item in the checklist and update the README Track R progress rows.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
