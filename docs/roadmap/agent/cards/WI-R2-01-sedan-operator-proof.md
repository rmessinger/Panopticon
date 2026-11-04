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

- Result: 2026-11-04 partial: accepted source harness and wrapper batch committed through 09b118bec (backend preparer 252c5e4da 86/86 both interpreters; unattended wrapper e11198e9e 388/388 venv, 378+10 stdlib; freeze path repair 5a56e9f04 101+32 both; interactive native proof harness 09b118bec 89 CPU tests pass, Interactive case LISTED but UNTESTED); one real cached preparation attempt post-freeze-fix reached 10:22:27 PRE-DEM and stopped exit 2 on a PROJ cs2cs DMS-format defect (old stage preserved, no semantic DEM comparison, no Core run, no CLI import, no Game); B's narrow decimal-format fix is UNREVIEWED and NOT in this batch; all scientific freeze tables and digest ea3e5f2e... unchanged; no numeric proof run, no proof PASS, no reviewed screenshots; Status stays `Ready`, no completion claimed.
- Result: 2026-11-04 partial: completed-freeze tile-path revalidation bug fixed (sedan_lidar_freeze.py reverify_completed now resolves tile locations through the integrity manifest rel_path exactly as the initial freeze does; five stage-2 tiles found at lidarhd/stage2/<LOT>/; fallback to pinned path when manifest entry absent; A-independent PASS ~/log/track-r/review-freeze-fix-20261104T1010/validation.log with 101 freeze + 32 integrity tests both interpreters, actual readonly reverify exit 0 with all six tiles verified and frozen artifacts unchanged before/after; hashes baa29552/489a3437); the actual Sedan proof has NOT been run and is not claimed; Status stays `Ready`, no completion claimed.
- Result: 2026-11-04 partial: unattended prepare-sedan-proof.sh wrapper + root-aware probe API + auto-WFS-lineage/tools bootstrap committed (8 C source/test files: sedan_lidar_acquire.py SOURCE_PINS one-entry probe update, sedan_lidar_probe.py first-class --cache-root, sedan_prepare_orchestrator.py + sedan_prepare_bootstrap.py + prepare-sedan-proof.sh + three test files; A-final review PASS ~/log/track-r/review-c8-final2-20261104T0800-stdlib.log and -venv.log with receipt-outputs 6-hash binding; 388 total scoped venv PASS 0 skip / stdlib 378 PASS 10 skip; hashes 37131973/ca1a1193/93c55be4/6a7311ec/5738cd58/9fd8baf4/f8558b25/aa0690ad); the REAL wrapper full-stage and proof run has NOT been executed and is not claimed; B's direct backend --prepare ran independently and is B's runtime, not this wrapper's; Status stays `Ready`, no completion claimed.
- Result: 2026-11-04 partial: post-freeze input preparation implemented (sedan_prepare_inputs.py: consumer-chain freeze reuse, playable/storage/source-corner geometry, generated-grid RGB reference; independent combined audit PASS ~/log/track-r/review-prepare-inputs-final-20261104T0830-0500/audit.md) with 86 scoped offline tests; actual data preparation (--prepare), Budget A, native run, and evidence are still pending; Status stays `Ready`, not a whole R2 proof pass.
- Result: 2026-11-03 partial: FINAL pre-DEM benchmark freeze complete and independently audited: canonical digest ea3e5f2e806b836c144dc33ffe6804dafcbad036abf240ffe7967aed589ea2f1; five PRIMARY sites across sectors s00, s02, s22 plus two alternates frozen; counts full 130,321 / canonical 128,881 / merged 127,449; the 1,432 missing cells are all E6 seam-ineligible with no eligibility loss (the earlier AOI-boundary cause is withdrawn); RSS 524,456 KiB is the CUMULATIVE child high-water, not six independent 512 MiB peaks; header original 68e... plus worker-only config 67c... consumer chain valid, producer RE-ENTRY check FAIL 4 by design constraint, so the final workflow must consumer-reuse and never claim false producer idempotence; numeric proof NOT RUN and no prepared MNT exists yet; science frozen unchanged (five plus two, Budget A 1.0 m engineering limit, Budget B 0.0390625 game-unit quantum pending actual proof); execution receipt ~/log/track-r/lidar-final-freeze-execution-20261103T2130/ (execution-receipt.md, execution-errata.md), independent audit ~/log/track-r/review-final-freeze-runtime-20261103T2230-0500/ (audit.md, addendum-arithmetic-and-e1-impact.md); Status stays `Ready`, no completion claimed.
- Result: 2026-11-03 partial: final six-tile freeze toolchain committed (sedan_lidar_freeze.py + sedan_lidar_integrity.py + sedan_lidar_header_worker.py + 94+32 offline guard tests; E-final PASS at ~/log/track-r/review-bl1bl2-delta-20261103T2215-0500/audit.md; hash ledger d65cc07c/760b3696/9fc6bacc/7688e5d3/579b29ef; whole venv suite 445 PASS 0 skip, stdlib 422 PASS 23 SKIP); banded worker 52e935b5 registered as the sole big-tile entrypoint (base math 5db92080, selector 4570c350, both drift-checked pre-spawn and bound in provenance); the FINAL measurement of all six tiles is PENDING at this commit - not a freeze PASS, not a numeric proof PASS, no DEM heights opened; Status stays `Ready`, no completion claimed.
- Result: 2026-11-03 partial: operator numeric tolerance SET before any DEM comparison: 1.0 m engineering acceptance limit, abs(payloadElevationMeters - frozen LiDAR referenceMeters) <= 1.0 m at every frozen primary across at least 3 sectors, all residuals and failures preserved, no site pruning, no failure-based alternate activation (2 pre-DEM-ranked alternates only); the manifest encodes tolerances.verticalMeters 1.0 with source.verticalAccuracyMeters absent (actual tile-specific RGE accuracy unknown; Core max-rule contract ab30059de unchanged; a documented actual accuracy above 1.0 m escalates before compare); 2024 auto-classification bias and 2012-2016 vs 2024 epoch carried unresolved; separate Budget B abs(E - (P - C)) <= 0.0390625 game units with Z0 the actual header origin, no 1.0 m term; the full six-tile freeze hash before DEM stays mandatory and the current first-tile 5 primaries / 4 sectors are preliminary only; policy SET is not quality resolution and no proof PASS is claimed (decision memo sedan-numeric-tolerance-20261103T1855.md, sha256 d4e25a8f94da4f3228ed22377bb738881be50f9e20342af10bf595c675d57cbc); Status stays `Ready`, no completion claimed.
- Result: 2026-11-03 partial: REAL v1.2 stage-1 first-tile measurement executed and PASSED (gate items only): argv precheck exit 0, then ONE foreground probe invocation 2026-11-03T18:20:50 to 18:21:17-05:00, exit 0, wall 26.89 s, MAX RSS 827,440 kB (about 808 MiB) under the frozen 1 GiB guard; gates a, b, c_v1_2, d_v1_2, e, f ALL PASS; c median 11.67 class-2 points/m2 over 8776 non-empty cells of the 9494 expected (historical v1.1 figures 11.61 and 9351 recorded, non-gating); the verbatim v1.1 totals 9494/8776/718 holes retained; d_v1_2 counted 5 preliminary primaries across 4 sectors (s00, s02, s12, s22) under the unchanged section 6 method; numeric_proof_pass stays FALSE, the final-six freeze is NOT done, and this is NOT the full proof; probe controller sha256 83efc9ea... (full hash in the E ledger) and first tile 19465caf...d212 unchanged; receipt ~/log/track-r/lidar-v12-firsttile-execution-20261103T1820-0500/execution-receipt.md; the remaining five tiles (701,880,508 bytes) are now eligible but NOT downloaded; Status stays `Ready`, no completion claimed.
- Result: 2026-11-03 partial: user-approved protocol v1.2 stage-1 gates c and d implemented and independently audited (criticals C1 class-2-only fit, C2 frozen 1 GiB true-peak live-set guard, C3 pre-serialization bound; E delta PASS at ~/log/track-r/review-v12-implementation-20261103T1720-0500/delta-acceptance.md); validation 239 tests PASS under the owned venv, 229 PASS plus 10 truthful stdlib skips (33 select, 135 probe); the REAL v1.2 first-tile measurement is PENDING at this commit; the v1.1 outcome (items a, b, c, e, f PASS; d FAIL 718 of 9494) stays historical and unmodified; numeric proof not run; Status stays `Ready`; remaining five tiles held.
- Result: 2026-11-03 partial: user approved protocol v1.2 as one package (owning deep dive section 4): stage-1 item c population is now the non-empty 10 m cells of the AOI-intersect-observed-first-tile-footprint expected set (median estimator and 4.0 threshold unchanged) and item d is the preliminary ground-availability floor of at least 5 eligible cells across at least 3 full-AOI sectors under the complete unchanged section 6 method, with the 10 m coverage map and all 718 hole attributions retained; the actual v1.1 measurement stands unchanged (a/b/c/e/f PASS, d FAIL 718 of 9494, overall NOT PASS); revised-gate implementation and real measurement PENDING, numeric tolerance and the whole proof pending; no PASS claimed; Status stays `Ready`, no completion claimed.
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
