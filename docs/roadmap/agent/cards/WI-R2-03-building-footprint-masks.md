# Task Card `WI-R2-03`: Building footprints as terrain masks

| Field | Value |
|---|---|
| Card ID | `WI-R2-03` |
| Roadmap phase | [Track R · R2](../../track-r-real-world-terrain.md): real-world structures as impassable regions |
| Status | `Done` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-R2-02` (Done; provides `prepare_area.py`, the `ign-fr` profile, and the receipt and ledger contract) |
| Diagrams | [§10.4 standalone import and dev preview flow](../../deep-dives/r-real-world-terrain.md#104-standalone-import-and-dev-preview-flow) |

## Goal

Give real-world buildings gameplay meaning without placing objects. The `ign-fr` footprint stage fetches BD TOPO building polygons from the IGN WFS into a driver-owned buildings KML and composes an operator manifest whose two rules make them impassable and unbuildable. Units in the Sedan preview then path around the old town's buildings. Elevation stays bare-earth and imagery stays flat (see deep dive §5.4). Delegate outputs are read, never modified.

## Context to read (and only this)

- `docs/roadmap/deep-dives/r-real-world-terrain.md` §5.3, §5.4 (vector footprints row and deferred list), §8.1, §10.4.
- `docs/roadmap/agent/cards/WI-B4b-16-feature-mapping.md`, `WI-B4b-17-terrain-masks.md`, `WI-B4b-23-payload-to-mapfile.md` (mask to `BlendTileData` passability).
- `docs/roadmap/agent/cards/WI-R2-02-provider-source-profiles.md` (Design table, profile fields, receipt and ledger rules), `scripts/geo/prepare_area.py`, `scripts/geo/providers/ign-fr.json`.
- `scripts/geo/sedan_prepare_bootstrap.py` lines 59-72 (in-repo WFS precedent: `DEFAULT_WFS_URL`, explicit User-Agent, caps). Read only.
- `src/Panopticon.Geospatial/Manifest/ManifestValidator.cs` (`ValidateTargetParameters`), `src/Panopticon.Geospatial/Features/FeatureMapper.cs` (layer name match, about line 231), `src/Panopticon.Geospatial/Readers/KmlReader.cs` (folder name match, about line 506), `src/Panopticon.Geospatial/Pipeline/SourceLoading/ImportSourceLoader.cs` (SelectExtent). Read only.
- `src/Panopticon.Geospatial/Generation/TerrainMaskGenerator.cs`, `TerrainMaskReport.cs`, `test/Panopticon.Geospatial.Tests/Generation/TerrainMaskGeneratorTests.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/ScenarioCaptureService.cs`, `src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs`, `scripts/annotate_capture_gif.py`, `docs/screenshots.md`.

## Design

| Item | Choice |
|---|---|
| Owned root | `<work dir>-footprints/` by default, or `--footprint-root <dir>`. Created with exclusive creation and never reused for a refresh. Holds the buildings KML, raw WFS pages, request log, provenance record, composed operator manifest, `sources.txt`, `preparation/receipt.json`, `commands.jsonl`, and `COMPLETED`. |
| Delegate boundary | The stage never writes to the extent KML, delegate rasters, delegate manifests (`sedan-ign-proof.json`, `preview-textures.json`), delegate `sources.txt`, or delegate markers in any existing root. It reads delegate outputs and verifies their receipt hashes. Warm delegate reuse keeps existing hash-bound roots valid, and any wrapper-owned writes it makes are recorded. |
| Operator manifest | Composed by the driver in the owned root. It references the unchanged delegate DEM, ortho, and extent KML by path, and the buildings KML as a separate vector source. `ManifestSource` carries no hash, so the SHA-256 of every referenced file is recorded in the provenance record and bound by the receipt. It holds two rules, because each rule has one target: a passability rule with `passable: false` and a buildability rule with `buildable: false`. Both set `sourceId` and `layerName` `buildings`. `ManifestValidator.cs` requires `passable` on passability rules and rejects `buildable` there, and applies the reverse to buildability rules. |
| Example fixture | `test/Panopticon.Geospatial.Tests/Examples/building-footprints.json` is a committed manifest fragment that shows the two rule shapes. A test deserializes the fragment and asserts the two rule shapes (target, geometry, `passable` or `buildable`, `sourceId`, `layerName`). The fragment is not composed into any run and is not the operator manifest. `Examples/sedan-ign-proof.json` is unchanged. |
| Layer name | The buildings KML places all polygons in one folder named exactly `buildings`, matching the rule `layerName` (`FeatureMapper.cs`, `KmlReader.cs`). |
| Source role | The buildings source uses role Vector (`ManifestSourceRole.Vector`). It is never extent-selected and needs no `extentFeatureId`. Extent selection still runs on the unchanged delegate extent source (`ImportSourceLoader.cs`). |
| Source | IGN BD TOPO `BDTOPO_V3:batiment` from the Géoplateforme WFS `https://data.geopf.fr/wfs/ows`, WFS 2.0.0 GetFeature, `outputFormat` `application/json`. Geometry MultiPolygon in EPSG:4326. Kept attributes: `cleabs` (stable feature ID), `nature`, `usage_1`, `usage_2`, `hauteur`. Licence Ouverte 2.0 (`lov2`). The service is live, not a pinned edition. |
| Acquisition | Bounded `curl` over HTTPS through the existing `run_tool` runner, with an explicit User-Agent, `--fail`, connect and total time limits, and a per-response size limit. JSON parsing and paging use the standard library. No `ogr2ogr`, `ogrinfo`, or other new GDAL vector dependency. |
| Request | `BBOX` covers the full storage extent plus apron, rounded outward, in EPSG:4326 with the axis order verified against returned coordinates. Paging uses `STARTINDEX` and `COUNT` with `SORTBY` `cleabs`. The first page confirms that the server honours sort and paging, or the stage fails. |
| Caps | Per-response bytes, total bytes, page count, feature count, and total wall-clock time. Defaults are set in the driver and recorded in the provenance record. Reaching any cap fails the stage. |
| Page rejection | Fail on: a `cleabs` already seen; a page that adds no new IDs before the reported `numberMatched` is reached; a truncated or unparseable response, or a `numberReturned` that differs from the feature count; a change in `numberMatched` between pages. Per-page response `timeStamp` values are recorded but never compared because the server mints them per request (verified live 2026-11-05). |
| Snapshot claim | Each page response timeStamp is recorded as observed, with first and last page stamps bounding the retrieval interval. The WFS edition is live and the paged set is never described as a consistent atomic snapshot. Provenance records each acquisition time and hash as observed. A later refresh is a new root. |
| Geometry | MultiPolygon only. Coordinates must be finite. Rings must be closed, have at least four positions, and not self-intersect. Holes are kept. The declared CRS must be EPSG:4326. Any other geometry type or invalid topology fails the stage and names the feature ID. Geometry is not clipped; the importer clips it. |
| Area and filter | Vertices are projected to the target WGS84 UTM zone through the `cs2cs` seam in batches. Area is the shoelace area of each shell minus its holes, in projected m2, never in degrees. Features below `--min-footprint-m2` (default 20) are dropped and counted as a dropped diagnostic, not as zero-cell footprints. |
| Provenance | Raw pages, request log, acquisition times, SHA-256 of each page and of the combined set, per-page counts, `numberMatched`, filter threshold, licence, and attribution go in the provenance record. `preparation/receipt.json` binds the buildings KML, provenance record, and composed manifest by hash. Attribution `IGN, Licence Ouverte 2.0` is also written to `sources.txt` and the manifest. |
| Reuse | The R2-02 contract applies: exclusive lock, STARTED and COMPLETED markers, verified receipt reuse. An incomplete footprint root fails with its stage preserved. A refresh is a new root. |
| CLI | Default preflight (no `--prepare`) is read-only: no writes, no network, no process spawn. It validates the profile and reports the planned footprint stage. Under `--prepare`, `ign-fr` runs the delegate and then the footprint stage by default. `--no-footprints` skips the stage and leaves the delegate outputs as they were. `--footprints` requests the stage explicitly; on a profile without the `footprints` capability it fails with a named error that names the profile and the capability. `--min-footprint-m2` sets the filter (default 20). `--footprint-root` sets the root. |
| `copernicus-glo30` | Unchanged. No `footprints` capability and no footprint support claim. This card does not require a Copernicus rerun. |
| Zero-cell counter | One additive `TerrainMaskReport` field, named `ZeroCellMappedPolygonFeatureCount`, whose documented scope is mapped mask polygons only, not all unmapped source features. It counts unique (`sourceId`, `featureId`) identities that intersect the in-map sampling domain and set no cell in any chunk under any applicable mask rule. A feature counts once even when two rules apply. A feature that sets a cell in any chunk is nonzero. Features filtered before mapping, and features outside or clipped away from the domain, are separate diagnostics. Empty or unmapped masks report 0. Holes are not coverage: a feature whose sample cells lie only in its own hole counts as zero-cell. |
| Breaking changes | None planned. If the counter needs a breaking change to an existing public API or report schema, stop and escalate before production code. No casual schema migrations. |
| Evidence | The operator run produces a mask overlay still with metrics and a scripted GIF of actual Move orders. Both are required by this card. The overlay renderer may be test-side. |

## Steps

1. Profile. In `scripts/geo/providers/ign-fr.json`, add the `footprints` block (WFS URL, `BDTOPO_V3:batiment`, `application/json`, `cleabs` as stable ID, kept attributes, geometry type, CRS, licence, attribution) and the `footprints` capability. Add validation for the `footprints` block: its download kind `wfs`, identifiers, and URLs, all checked before any request is built. Do not add `wfs` to the elevation or imagery download kinds. `copernicus-glo30.json` is unchanged.
2. Footprint stage in `prepare_area.py`. Create the owned root exclusively. Run the `ign-fr` delegate in warm reuse, verify that the delegate receipt still verifies, and record any wrapper-owned writes the warm call makes to the delegate root. Fetch pages per the Design rows, validate geometry, project and filter, write the `buildings` folder KML, write the provenance record and `sources.txt`, compose the operator manifest, then write the receipt, ledger, and `COMPLETED`. `prepare-sedan-proof.sh` and `sedan_*` stay unchanged; the stage does not reimplement the delegate.
3. CLI. Implement the preflight plan line, `--footprints`, `--no-footprints`, `--min-footprint-m2`, `--footprint-root`, and the named capability error.
4. Example fixture. Add `test/Panopticon.Geospatial.Tests/Examples/building-footprints.json` with the two rule shapes and a vector source reference. Add a test that deserializes it and asserts the two rule shapes (target, geometry, `passable` or `buildable`, `sourceId`, `layerName`). It is not composed into any run. Do not edit `Examples/sedan-ign-proof.json`.
5. Zero-cell counter. In `TerrainMaskGenerator.cs` and `TerrainMaskReport.cs`, track unique feature identities across chunks and rules and emit the counter per the Design row. Commit separately from steps 2 to 4 and 6. Escalate first if it needs an existing API break.
6. Mask tests. Add `test/Panopticon.Geospatial.Tests/Generation/BuildingFootprintMaskTests.cs`. Run a synthetic KML with a 30 m square and a 2 m square through `FeatureMapper` and `TerrainMaskGenerator` at W3a scale. The 2 m square goes directly to mapping although the 20 m2 driver filter would drop it. Assert that the 30 m interior cells are `Impassable | Unbuildable`, cells outside are clear, and the 2 m square is counted once as zero-cell. Add cases, in this file or `TerrainMaskGeneratorTests.cs`, for: a footprint that sets cells in one chunk and none in another is nonzero; a hole-only feature is zero-cell; an empty mask reports 0; a feature under both rules is counted once.
7. Script tests. Extend `scripts/geo/tests/test_prepare_area.py` offline, with `run_tool` and `cs2cs` stubbed. Cover: footprint argv; bbox and area filter; `--no-footprints`; `--footprints` on `copernicus-glo30` (named error); preflight writes and spawns nothing; each page rejection; each cap; geometry rejections; projected-area filtering; provenance and receipt contents; the delegate receipt still verifies after a run.
8. Operator run, after the offline suites pass. Run the read-only preflight, then `--prepare` with footprints on against the warm `ign-fr` delegate root, writing into a fresh footprint root. Verify that the delegate receipt still verifies and that the delegate's bound outputs are hash-identical, and list any wrapper-owned writes. Then run `geoimport import --manifest <composed manifest> --out <payload>` and `geoimport report --payload <payload>`. Record footprint count, dropped count, zero-cell footprints, masked cell count and share, and the WFS timestamps and hashes. Do not rerun the R2-01 scientific proof or refreeze the LiDAR truth. Run `git status --porcelain` and confirm that only intended files appear; no WFS response, raw page, KML, or other data file may be added.
9. Mask overlay and GIF. Render the mask overlay still with metrics over the old town using the test-side renderer. Build a ScenarioCapture profile with `MapPath` set to the imported preview map, issue real Move orders to a unit group across the old town, and render the GIF with `scripts/annotate_capture_gif.py`. Assert per frame that no unit stands in an impassable cell, that no unit or object spawns beyond the expected set, and that every mask-derived placement derives from footprint geometry. No teleports and no interpolated paths. Store evidence per `docs/screenshots.md` and record results in deep dive §8.1.

## Files likely to change

- `scripts/geo/prepare_area.py`, `scripts/geo/providers/ign-fr.json`
- `scripts/geo/tests/test_prepare_area.py`
- `test/Panopticon.Geospatial.Tests/Examples/building-footprints.json` (new) and its fragment test
- `test/Panopticon.Geospatial.Tests/Generation/BuildingFootprintMaskTests.cs` (new), `TerrainMaskGeneratorTests.cs`
- `src/Panopticon.Geospatial/Generation/TerrainMaskGenerator.cs`, `TerrainMaskReport.cs` (step 5)
- Operator run only: a ScenarioCapture profile and a test-side overlay renderer, not committed with IGN-derived data
- `docs/roadmap/deep-dives/r-real-world-terrain.md` §8.1 (results)

Not changed: `scripts/geo/prepare-sedan-proof.sh`, `scripts/geo/sedan_*`, `scripts/geo/providers/copernicus-glo30.json`, `test/Panopticon.Geospatial.Tests/Examples/sedan-ign-proof.json`, the Copernicus path, and the extent KML.

## Acceptance criteria

- [x] `prepare_area.py --provider ign-fr --prepare` writes the buildings KML, provenance record, `sources.txt`, composed operator manifest, and receipt into a fresh footprint root. The delegate root's extent KML, rasters, manifests, `sources.txt`, and markers are hash-identical before and after, and any wrapper-owned writes are listed in the provenance record.
- [x] Warm delegate reuse leaves existing hash-bound roots valid, and the delegate receipt still verifies.
- [x] Preflight without `--prepare` writes nothing, spawns nothing, makes no network request, and reports the planned footprint stage.
- [x] `--no-footprints` reproduces the delegate behaviour. `--footprints` on `copernicus-glo30` fails with the named capability error. The Copernicus path is unchanged and needs no rerun.
- [x] WFS acquisition rejects duplicate IDs, no-progress pages, truncated responses, and inconsistent pages; enforces every cap; and records the Design provenance. The snapshot is reported as observed, never as consistent.
- [x] The synthetic mask tests pass, and the zero-cell counter matches the Design semantics in every case listed in step 6.
- [x] The operator run records the mask overlay still with metrics and the scripted GIF. The GIF uses only actual Move orders, and the per-frame assertions in step 9 pass.
- [x] The operator cleanliness check (`git status --porcelain`) shows only intended files; no WFS response, raw page, buildings KML, or other data file is added.
- [x] Standard validation passes.

## Validation commands

```sh
python3 -m unittest discover -s scripts/geo/tests -p "test_prepare_area.py"
python3 -m unittest discover -s scripts/geo/tests
python3 scripts/geo/prepare_area.py --help
python3 scripts/geo/prepare_area.py --provider ign-fr --center 49.702,4.947 --size 1100 --name sedan-proof "<work dir>/ign"
dotnet build src
dotnet test test/Panopticon.Geospatial.Tests --filter "FullyQualifiedName~BuildingFootprint|FullyQualifiedName~Manifest|FullyQualifiedName~TerrainMask"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

The preflight example writes nothing. `--prepare` runs and the `--footprints` run on `copernicus-glo30`, which must fail with the named capability error, are operator-only and never run in CI.

## Out of scope / guardrails

- Placing structure objects, 3D building geometry, building heights as geometry, and bridges (deferred to R3, see §5.4). Heights are kept as provenance attributes only.
- OpenStreetMap footprints (ODbL share-alike needs its own licence decision).
- Changing the bare-earth elevation or flattening imagery under footprints.
- New manifest target kinds; the existing Passability and Buildability regions suffice.
- Any write to the extent KML, delegate rasters, delegate manifests, delegate `sources.txt`, or delegate markers, and any write into an existing root.
- New vector dependencies (`ogr2ogr`, `ogrinfo`, GDAL vector bindings).
- Claims of a pinned BD TOPO edition or of a consistent server snapshot. A refresh is a new root.
- Footprint support for `copernicus-glo30`, and any Copernicus rerun for this card.
- An R2-01 scientific proof rerun, a LiDAR refreeze, or any new 1 m or 4 m PASS claim.
- Committing WFS responses, raw pages, the buildings KML, or any IGN-derived output. This is policy; the operator cleanliness check in step 8 verifies it.
- Schema migrations or breaking public API changes without escalation.

## On completion

- Keep Status `Ready` until implementation starts. Set `Done` or `Blocked` only with the Architect's direction.
- Tick the R2 building footprints item in the checklist and update the README Track R progress rows only on `Done`.
- Add a `Result:` line only on completion: date, what changed, validation outcome, follow-up cards.

Result: 2026-11-05. Added the `ign-fr` `footprints` block and capability, the footprint stage in `prepare_area.py` (paged WFS fetch, caps, projected area filter, provenance, receipt), the `--footprints`, `--no-footprints`, `--min-footprint-m2`, and `--footprint-root` CLI, the `ZeroCellMappedPolygonFeatureCount` counter, and `BuildingFootprintMaskTests.cs`. The accepted v3 operator run wrote the buildings KML, provenance record, `sources.txt`, composed manifest, and receipt into a fresh root, left the delegate root hash-identical, and verified warm delegate reuse. The overlay still and outlines-v2 GIF were rendered from actual Move orders, and the per-frame checker (Checks 1 to 6, 3600 samples) exited 0. This external evidence is not committed, because the card forbids committing IGN-derived data. Validation: accepted full suite with the real install (`CNC_GENERALS_PATH` set, 2026-11-06): 4755 passed, 11 failed, 8 skipped, 4774 total across 6 assemblies (Mathematics 53; WorldBuilder.Core 15; Preview 40; Geospatial 1284; ReplaySketch 520; Simulation 2843 passed, 11 failed, 8 skipped, 2862 total). The failures are the 10 known-manifest entries TF-01 to TF-10, all executed and none skipped or resolved, plus `GroupMoveOrderTests.SquaddedSelection_CostsOneSharedLeaderPath`, an order-dependent Navigation-collection flake that passes in isolation and on alternate schedules, with no Geospatial reference or production Simulation change in the diff. Against the PR318 baseline of 4711 passed, 10 failed, 8 skipped, 4729 total, the delta is 45 added tests (40 Geospatial and 5 ReplaySketch, all passing) plus the one flaky failure. The earlier no-install run (4681 passed, 2 failed, 7 skipped, 4690 total; TRX under `~/log/track-r/r2-03-final-20261105T233053-0500/trx/`) was rejected as misconfigured because the install environment was unset, and is superseded, not accepted. Geo stdlib 811 (788 passed, 23 skipped); geo venv 811 passed; `scripts/tests` 53 of 53; solution build 0 errors; `dotnet format` verify clean. Step 4's literal fixture `test/Panopticon.Geospatial.Tests/Examples/building-footprints.json` was not created and is superseded: its intent (both rule shapes and deserialization) is covered by `ManifestLoaderTests`, the exact-dict assertions on both building rules in `test_footprints.py`, and the `FeatureMapperTests` propagation tests. WFS rejection and cap evidence is in `scripts/geo/tests/test_footprints.py`: `test_wfs_duplicate_cleabs_rejected`, `test_wfs_no_progress_rejected`, `test_wfs_truncated_body_rejected_without_publish`, `test_wfs_features_exceeding_number_matched_rejected`, `test_wfs_number_returned_mismatch_rejected`, `test_wfs_number_matched_change_rejected`, and the per-response, total-bytes, page, feature, and timeout cap tests. A live three-page sorted run recorded per-request timestamps as observed. Follow-up: `WI-R3-01` unblocked. Evidence and limits: [deep dive §8.1](../../deep-dives/r-real-world-terrain.md#81-early-operator-proof-r2).
