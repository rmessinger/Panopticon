# Task Card `WI-R2-01`: Sedan real-world preview proof

| Field | Value |
|---|---|
| Card ID | `WI-R2-01` |
| Roadmap phase | [Track R · R2](../../track-r-real-world-terrain.md): early operator proof on the dev preview |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `M` |
| Prerequisites | `WI-B4b-24` (which needs `WI-B4b-19`, `WI-B4b-20`, `WI-B4b-23`) |
| Diagrams | [§10.4 standalone import and dev preview flow](../../deep-dives/r-real-world-terrain.md#104-standalone-import-and-dev-preview-flow) |

## Goal

Import one small real area of Sedan (Ardennes, France) from IGN data and a generated KML extent, pass independent control points, and play the generated preview map in Generals. This is the first real-data run of the R1/R2 pipeline, ahead of the R5 package and WorldBuilder proof.

## Context to read (and only this)

- `docs/roadmap/deep-dives/r-real-world-terrain.md` §2 (source boundary), §4 (coordinates, datum, tolerances), §6.1 (payload and preview), §8 (validation layers, including §8.1 added with this card).
- `docs/roadmap/deep-dives/r2-sedan-ground-truth.md` (owns the LiDAR HD ground-truth protocol, its staged gates, and the frozen pre-DEM selection numerics).
- `test/Panopticon.Geospatial.Tests/Examples/worldcover-materials.json` (manifest shape).
- `src/Panopticon.Tools.GeoImport/` and the `WI-B4b-24` game-backed preview test.
- `docs/screenshots.md`.

## Area and sources

| Item | Choice |
|---|---|
| Area | About 1.1 km square centred on the Château fort de Sedan (49.702 N, 4.947 E), including the old town and the Meuse; inside the W3a Sedan extent |
| Extent and control-point placemarks | Polygon `sedan-proof-extent` (a 1100 m square in EPSG:32631 around the centre) and named ground-truth-cell placemarks, generated as KML (EPSG:4326) by the preparation script |
| Elevation | IGN RGE ALTI 1 m, department 08 (Licence Ouverte 2.0), Lambert-93, datum `NGF-IGN69`; stated vertical accuracy from the tile quality metadata |
| Imagery | IGN BD ORTHO 20 cm, department 08 (Licence Ouverte 2.0), Lambert-93 |
| Vertical control | at least 5 blinded ground-truth cells across at least 3 distinct AOI sectors, derived from LiDAR HD class-2 points (mission 24LHDOD, flown 2024-11-30, IGN69 normal heights, Licence Ouverte 2.0) inside the extent, selected by the frozen pre-DEM protocol recorded in the sedan-alternative-groundtruth record; fewer than 5 cells or fewer than 3 distinct sectors is STOP and escalate; the levelling-benchmark audit of 2026-11-02 is the documented reason no benchmarks qualify. |
| Target | `utm` zone 31 north (EPSG:32631), `metersPerGameUnit` 0.3048 (W3a scale, 3.048 m lattice), vertical exaggeration 1 |

Google basemap imagery and terrain are not used (see §2). No source file, derived raster, payload, or `.map` is committed.

The whole run is command-line only: `curl`, `7z`, and GDAL 3.x command-line tools (`gdaltransform`, `ogr2ogr`, `ogrinfo`, `gdalbuildvrt`, `gdalwarp`) with the JP2OpenJPEG driver for BD ORTHO. No desktop application is required. Google Earth Pro may open the generated KML for a visual check; it is not a step.

## Steps

1. Add `scripts/geo/prepare-sedan-proof.sh <work dir>`, an idempotent operator script (never run in CI) that writes everything under the work dir:
   - Centre: `gdaltransform -s_srs EPSG:4326 -t_srs EPSG:32631` on `4.947 49.702`; extent = centre ± 550 m in EPSG:32631; warp window = extent + 50 m.
   - Downloads: list the IGN Géoplateforme download service (`https://data.geopf.fr/telechargement/`) resources for RGE ALTI 1 m and BD ORTHO 20 cm, select the department 08 (`D008`) archives, fetch with `curl -fL -C -` (resumable), and extract with `7z x`. Record archive names and edition dates in a `sources.txt` beside the data.
   - Elevation: `gdalbuildvrt -a_srs EPSG:2154` over the RGE ALTI tiles, then `gdalwarp -t_srs EPSG:32631 -tr 1 1 -r bilinear -te <warp window> -ot Float32 -co COMPRESS=NONE` to a classic single-image GeoTIFF.
   - Imagery: `gdalbuildvrt` over the BD ORTHO JP2 tiles, then `gdalwarp -t_srs EPSG:32631 -tr 1 1 -r average -te <warp window> -ot Byte -b 1 -b 2 -b 3 -co COMPRESS=NONE`.
   - Ground truth: fetch the 6 LiDAR HD COPC tiles listed in the Sedan ground-truth deep dive into the cache (first tile 0840_6958 with its verification gate, then the rest), run the frozen pre-DEM selection protocol with its STOP-and-escalate floor of 5 primaries across 3 distinct sectors, and record the frozen table hashes, tile hashes, and tool manifest in `sources.txt`.
   - KML: write the extent polygon (`sedan-proof-extent`, transformed back to EPSG:4326) and one placemark per selected ground-truth cell into `sedan-proof.kml` with `ogr2ogr -f KML`.
   - Print the selected cells' identifiers, lat/lon, and truth elevations for the manifest's `controlPoints`.
2. Add `test/Panopticon.Geospatial.Tests/Examples/sedan-ign-proof.json`: sources with relative paths, labels, and attribution (`IGN, Licence Ouverte 2.0`; `generated extent KML`), DEM `verticalDatum` `NGF-IGN69` with its stated `verticalAccuracyMeters`, `vertical.originMeters` near the extent minimum, the target and scale above, `raster.noData` `nearest-valid`, and `controlPoints` (added by `WI-B4b-20`):
   - each selected ground-truth cell with lat, lon, and expected elevation = the robust-plane value at the evaluation coordinate (`NGF-IGN69` metres); vertical tolerance per the committed max rule with the numeric explicit tolerance from the separate operator decision; no mark-height allowance; horizontal left unevaluated.
   - two imagery points with expected RGB and per-channel tolerance: Meuse water and one large bright roof or square;
   - horizontal game XY left unevaluated unless an independent coordinate source is supplied (§6.1).
3. Add a manifest test that loads `sedan-ign-proof.json` with `ManifestLoader` and `ManifestValidator` without source files present and asserts no schema errors, so the example cannot rot.
4. Run `GeoImport import --manifest sedan-ign-proof.json --out <payload>` and `GeoImport report --payload <payload>`. Expected: 3 x 3 chunks, bilinear or area-weighted kernels recorded, no unfilled no-data, all control points pass.
5. Run `GeoImport preview --payload <payload> --textures <map.json> --out <maps dir>`, then an Interactive test `SedanOperatorProofTests` that reads `PANOPTICON_GEO_PROOF_DIR`, starts a skirmish on the generated map the same way the `WI-B4b-24` test does, runs 300 logic frames, and asserts terrain height at each ground-truth cell within tolerance after restoring the preview Z offset.
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
- [ ] Operator run: every selected ground-truth cell passes its vertical tolerance with all residuals retained, both imagery points pass, and the report records `NGF-IGN69` and the kernels.
- [ ] The preview map loads in Generals and runs 300 logic frames with ground-truth cell heights within tolerance.
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

- Result: 2026-11-03 partial: schema-only data-free example manifest and CI guard tests committed (test/Panopticon.Geospatial.Tests/Examples/sedan-ign-proof.json sha256 2f4a9c1a, test/Panopticon.Geospatial.Tests/Manifest/SedanIgnProofManifestTests.cs sha256 e7eb7924, both full hashes in the integration receipt); the example validates through the real ManifestLoader with zero schema errors and no source data (UTM 31N, 0.3048 m/game unit, NGF-IGN69, nearest-valid with an ILLUSTRATIVE 25 m template radius, edge fail); its seven controls are EXAMPLE ONLY - NOT MEASURED placeholders with zero elevations and pure-math coordinates, and its vertical tolerance is a validator-required placeholder, NOT the operator numeric decision; validation 7/7 focused and 1227/1227 whole Geospatial plus format/brace/whitespace checks; the real controls, the numeric tolerance, and the fill radius must be regenerated and approved by the real prepare workflow, the example must never be used at runtime, and the real Sedan proof remains NOT complete; Status stays `Ready`, no completion claimed.
- Result: 2026-11-03 partial: user-approved corrected six-tile budget pinned (830532133 bytes total, +69398255 over the old 761133878; corrected set E 0839/0840/0841 x N 6958/6959 with the 1887 of 12321 old-set AOI cell omission recorded; first-tile pin `0840_6958` unchanged at 128651625 bytes; remaining five 701880508 bytes, still NOT authorized); the card's tile-list reference now points at the owning deep dive instead of the external record; probe source catalogue and test expectations updated metadata-only, fetch behaviour unchanged; Status stays `Ready`, no completion claimed.
- Result: 2026-11-03 partial: first-tile physical section 5 gate RUN and FAILED honestly: gates a/b/c/e/f PASS, gate d FAIL with 718 of 9494 sampled cells having no ground return; overall physical gate NOT PASS, numeric tolerance decision pending, and the other five tiles NOT authorized; provenance and the probe report receipt live under ~/log/track-r/lidar-firsttile-execution-20261103T1330-0500 (probe-report.json); the earlier withdrawn 0.2 m accuracy claim and percentage figures are corrected only in the external receipt and none is recorded here; the preceding Result bullets are preserved as historical record; Status stays `Ready`, no completion claimed.
- Result: 2026-11-03 partial: first LiDAR HD tile acquired and verified (LHD_FXX_0840_6958_PTS_LAMB93_IGN69.copc.laz, 128,651,625 bytes, local sha256 19465caf3085665f94fd648315896a84cbcf8fd2d9777eca85800c131979d212; no publisher checksum claimed) with fresh verbatim WFS lineage; worker laspy API regression fixed (chunk_iterator points_per_iteration) and proven by a real-library synthetic smoke (85 tests OK under the owned venv, 83 pass + 2 truthful stdlib skips; acquire 19; full suite 156); the ONE physical section 5 probe is PENDING and runs after this commit; Status stays `Ready`, no completion claimed.
- Result: 2026-11-03 partial: stage-1 LiDAR first-tile probe sources committed (controller, worker, 83 offline guard tests; independent safety review PASS, 83 probe + 19 acquire + 5/5 harness + 4/4 oracles, ~/log/track-r/review-lidar-firsttile-20261103T1145-0500/final-reverify-nf1.md); isolated laspy/lazrs tooling install plus the single 128,651,625-byte first-tile fetch and physical section 5 gate now executing under the reviewed runtime plan; gate outcome pending here; Status stays `Ready`, no completion claimed.
- Result: 2026-11-03 partial: user-approved ground-truth protocol amendment committed in
  2a008bc01 (owning deep dive `docs/roadmap/deep-dives/r2-sedan-ground-truth.md`, four dictated
  card fragments, Track R and checklist phrase updates); doc validation 9/9 plus 26 synthetic
  prototype tests PASS; first-tile physical gate (GATE 2), final numeric tolerance (GATE 3), and
  the proof run pending; Status stays `Ready`, no completion claimed.
- Set Status above to `Done` or `Blocked` with reason.
- Tick the R2 Sedan proof item in the checklist and update the README Track R progress rows.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
