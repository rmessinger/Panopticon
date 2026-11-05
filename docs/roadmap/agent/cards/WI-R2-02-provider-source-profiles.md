# Task Card `WI-R2-02`: Provider source profiles for data preparation

| Field | Value |
|---|---|
| Card ID | `WI-R2-02` |
| Roadmap phase | [Track R · R2](../../track-r-real-world-terrain.md): operator data preparation beyond France |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `M` |
| Prerequisites | `WI-R2-01` (Done; its preparation modules are the `ign-fr` delegate target) |
| Diagrams | [§10.4 standalone import and dev preview flow](../../deep-dives/r-real-world-terrain.md#104-standalone-import-and-dev-preview-flow) |

## Goal

Add `scripts/geo/prepare_area.py`, a provider-neutral driver that prepares a bounded area from a named provider profile. Two profiles ship: `ign-fr`, which delegates to the accepted Sedan preparation pipeline and reproduces the `WI-R2-01` Sedan inputs, and `copernicus-glo30`, a Europe-wide fallback that prepares the same area from public Copernicus data. The driver does not replace the Sedan wrapper or orchestrator. Every profile ends in the existing importer inputs: one EPSG:4326 KML extent, one EPSG:32631 (or other WGS84 UTM) Float32 DEM, and one optional uint8 RGB raster. The importer and manifest schema are unchanged.

The `copernicus-glo30` path is an evaluation of a lower-resolution surface model against the frozen Sedan LiDAR truth. It is not a bare-earth substitute for IGN RGE ALTI, and it does not claim to meet the 1 m or bare-earth requirements of Sedan.

## Context to read (and only this)

- `docs/roadmap/agent/cards/WI-R2-01-sedan-operator-proof.md` (Done; steps 1 and 7; Result lines).
- `scripts/geo/prepare-sedan-proof.sh`, `scripts/geo/sedan_prepare_orchestrator.py` (header and `build_parser`), `scripts/geo/sedan_prepare_inputs.py` (header, grid, warp argv, manifest builders).
- `docs/roadmap/deep-dives/r-real-world-terrain.md` §2 (source boundary), §4 (coordinates, datum, tolerances, no-data, resampling), §5.4 (bare-earth profiles), §8.1.
- `docs/roadmap/open-questions.md` B4b source boundary, B4b pipeline decisions 2 to 4, and the structures decision (bare-earth profiles).
- `src/Panopticon.Geospatial/Manifest/ManifestValidator.cs` (datum and tolerance rules) and `src/Panopticon.Geospatial/Pipeline/SourceLoading/ImportSourceLoader.cs` (datum mismatch and mixed-datum failures). Read only; no C# edits.

## Design

| Item | Choice |
|---|---|
| Driver | `scripts/geo/prepare_area.py`, Python 3 standard library only. Runs the external tools `curl`, `gdalinfo`, `gdalwarp`, `gdal_translate`, and `cs2cs` through one runner. No GDAL Python bindings, no `7z` in the Copernicus path. |
| Ign-fr path | Delegates to the accepted Sedan pipeline (`prepare-sedan-proof.sh` and its orchestrator). The driver supplies the centre, size, and name and does not port the existing logic. `sedan_prepare_orchestrator.py` (1028 lines) and `sedan_prepare_inputs.py` (2210 lines) are not ported. Shared helpers are extracted only when small and behaviour-neutral. |
| Invocation | `prepare_area.py --provider <id> --center <lat>,<lon> --size <meters> --name <area> [--utm-zone <1-60>] [--prepare] <work dir>`. Default mode is read-only preflight: it validates the profile, the centre, and the coverage, prints the plan, writes nothing, and spawns no download or GDAL process. `--prepare` is the explicit write operation. |
| Root resolution | Work dir: positional > env `PANOPTICON_GEO_PROOF_DIR` > error. Preflight (no `--prepare`) writes nothing and spawns nothing. Only `--prepare` writes. |
| Profiles | `scripts/geo/providers/<id>.json`, one per provider. Profile data only: no provider logic in the driver beyond the named download kinds (`tile-grid`, `stac`, `delegate`). Profile parsing rejects invalid, non-finite, or untrusted identifiers before any path is built or network request is made. |
| Target CRS | WGS84 UTM. Zone from the centre longitude, `floor((lon + 180) / 6) + 1`; hemisphere from the centre latitude (north: EPSG:326zz, south: EPSG:327zz). `--utm-zone` overrides the zone explicitly and is recorded. Zone edges, hemisphere, and override are tested. Polar centres, antimeridian-crossing extents, and cross-zone extents are rejected with a named error until implemented. |
| Grid | Sedan equivalence: 1100 m playable AOI, 1170.432 m storage (3 x 3 chunks of 128 intervals at the 3.048 m lattice, 385 lattice points per axis, one-sample apron), `nativeGSD` source resolution. Other centres and sizes derive the chunk grid through the same import contract: `n_chunks = ceil(size / chunk_m)`, outward from the south-west corner, with the source-corner margin equal to `HALF_FOOTPRINT + REACH` (4.572 m). No 50 m buffer. |
| Outputs | `<name>` maps to `sedan-dem-32631.tif`, `sedan-ortho-32631.tif`, `sedan-extent.kml`, `sedan-ign-proof.json` (manifest), `preview-textures.json`, `sources.txt`, `preparation/receipt.json`, `preparation/commands.jsonl` (ledger). The Copernicus profile writes the same names with the manifest named `<name>.json`. Outputs are never overwritten; a completed receipt is verified and skipped and failures preserve the stage. |
| Idempotence | Stage directory, lock file, STARTED and COMPLETED markers, content hash per artefact, verify-and-skip on a completed receipt, resume of partial downloads with `curl -fL -C -`, and cleanup of the stage directory on failure. Operator files are never overwritten. Declared free-space guard runs before any write. |
| Coverage | Each profile declares a WGS84 coverage box and a capability list. Coverage is checked for the full storage extent and apron, not for the centre alone, before any download. A failure names the profile and the uncovered corner. |
| Scaling | Copernicus RGB: linear reflectance `0..0.2` maps to `0..255`, applied only after the asset scale and offset are read from the STAC item. Baseline offsets are honoured and never guessed or double-applied. Ambiguous or missing scale metadata fails. |

Profile fields:

| Field | Meaning |
|---|---|
| `id`, `label` | Provider identifier and display name. Validated before use. |
| `coverage` | Provider geographic coverage (WGS84 box). Distinct from adapter capabilities. |
| `capabilities` | What the adapter supports for this profile (for example `elevation`, `imagery`, `bareEarth`, `surfaceModel`, `stacImagery`). |
| `elevation.download` | Kind (`tile-grid`, `delegate`), base URL, tile naming pattern, region rule, and pinned edition or archive names. |
| `elevation.strategy` | `full` (download the whole product, record file SHA-256) or `window` (bounded GDAL range read, record window, STAC query, and item asset URL with ETag, LastModified, and ContentLength). |
| `elevation.sourceCrs`, `verticalDatum`, `verticalAccuracyMeters` | Source CRS from embedded headers. Datum string written to `sources.txt` and the manifest (`EGM2008` for GLO-30). Accuracy is stated only when the source states it, otherwise omitted. |
| `elevation.surfaceType` | `bareEarth` (IGN RGE ALTI) or `surfaceModel` (GLO-30 DSM). Written to the manifest label and `sources.txt`. |
| `elevation.resampling` | `bilinear` for DEM output. |
| `elevation.noData` | `nearest-valid`, radius pinned to 3.048 m, `edgePolicy` fail. Never silently widened. No-data is never swept under resampling. |
| `imagery` | Optional. For Copernicus: STAC collection, date window, `eo:cloud_cover` limit (default `<= 20`), band order (red, green, blue, explicit), asset keys, scale, offset, nodata handling, `imageryPixelsPerSample` 10. Selection is deterministic: all pages, least cloud among suitable complete-AOI items, stable item-ID tie-break; the query, item, asset URL, and hash are recorded. |
| `licence` | Full obligations for the source and for derived outputs (see below). Copied into `sources.txt` and printed by the driver. |
| `attribution` | Source notice and derived-output notice, quoted exactly. |

Licence and obligation facts (quoted from the licence texts; any profile that omits them fails):

| Source | Facts |
|---|---|
| Copernicus DEM GLO-30 | Licence: `COP-DEM-GLO-30-F` (Free & Open), licence PDF `https://copernicus-dem-30m.s3.amazonaws.com/Copernicus_DSM_COG_10_N49_00_E004_00_DEM/INFO/eula_F.pdf`, registry `https://registry.opendata.aws/copernicus-dem/`, readme `https://copernicus-dem-30m.s3.amazonaws.com/readme.html`. Rights: reproduction, distribution, communication to the general public, adaptation and combination, worldwide, unlimited in time, free of charge (Art. 3 to 5). Source notice (Art. 6a): `© DLR e.V. 2010-2014 and © Airbus Defence and Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA; all rights reserved`. Derived notice (Art. 6b, for adapted or modified data): `produced using Copernicus WorldDEM-30 © DLR e.V. 2010-2014 and © Airbus Defence and Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA; all rights reserved`. Liability sentence (Art. 6c, to be carried into any redistribution notice): `The organisations in charge of the Copernicus programme by law or by delegation do not incur any liability for any use of the Copernicus WorldDEM-30`. No endorsement (Art. 6d). Subsequent users bound by the same obligations (Art. 6e). Supplied as is, at the user's own responsibility, with user indemnity (Art. 7). IPR stays with the Provider (Art. 9). Licence terminates on breach (Art. 9, termination). Product facts: DSM from TanDEM-X 2011 to 2015, under 4 m 90% linear error (LE90) vertical and under 6 m horizontal, EPSG:4326 with EGM2008 heights (EPSG:3855 vertical), Point sampling. Tile N49_E004 is 3600 x 3600 Float32 COG, about 44 MB; a 59 x 37 window is about 9 KB and 145 to 228 m. |
| Sentinel-2 L2A (E84 STAC) | Legal notice `https://sentinels.copernicus.eu/documents/247904/690755/Sentinel_Data_Legal_Notice`. Free, full, and open access for reproduction, distribution, communication to the public, adaptation, and combination, with no warranty of quality or suitability. Source notice: `Copernicus Sentinel data [Year]`. Adapted or modified data notice: `Contains modified Copernicus Sentinel data [Year]`. User renounces damages claims against the European Union and the providers. Catalogue `https://earth-search.aws.element84.com/v1/collections/sentinel-2-l2a`, bucket `https://sentinel-cogs.s3.us-west-2.amazonaws.com/` (public HTTPS, no RequesterPays; `s3://sentinel-s2-l2a` is not used). |
| IGN RGE ALTI and BD ORTHO | Licence Ouverte 2.0 (Etalab). Attribution `IGN, Licence Ouverte 2.0` in `sources.txt` and the manifest. Pinned products in `scripts/geo/sedan_acquire.py`. |

Where the source notice and the derived notice differ, both are recorded in `sources.txt`, and the derived notice applies to every output that is adapted, resampled, or combined.

Manifest minimums for every profile: target `utm` with zone 1 to 60 and hemisphere required; `verticalDatum` is any string, with `unknown` as the default and `EGM2008` allowed; `verticalAccuracyMeters` is optional, positive, and finite, and is omitted when unknown; `tolerances.verticalMeters` is required when the datum is known and no accuracy is stated (`ManifestValidator.cs` 446 to 449), even when the import has no control points. Copernicus sets it to 1.0 as a schema-required value that is not evaluated, and no 4 m value is derived from the LE90 figure. `controlPoints` is optional. `rasterPolicy` is `nearest-valid` with a 3.048 m radius and `edgePolicy` fail, and `imageryPixelsPerSample` is 10. The KML contains exactly one polygon. The importer performs no geoid conversion and no bare-earth check (`ImportSourceLoader.cs` 238 to 275), so the profile must state the surface type.

## Steps

1. Add `prepare_area.py` skeleton: argument parsing, profile loading and validation, read-only preflight, UTM zone and grid rules, extent KML, warp argv builders, `sources.txt`, ledger, STAC query and selection, tiling and scaling, and output validation. The Sedan delegate is the only ign-fr write path.
2. Add `providers/ign-fr.json` with the Sedan pins (RGE ALTI 1 m `RGEALTI_2-0_1M_TIF_LAMB93-IGN69_D008_2019-10-14`, BD ORTHO 20 cm `BDORTHO_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01`, from `sedan_acquire.py`), the delegate kind, and coverage that must contain the full storage extent and apron. Surface type `bareEarth`. No department lookup and no new assets.
3. Add `providers/copernicus-glo30.json`: GLO-30 tile-grid from the public AWS bucket, one 1 degree tile per request. DEM strategy is a full tile download with recorded SHA-256 (preferred) or a bounded window read (recording the window, STAC query, asset URL, ETag, LastModified, and ContentLength). Never claim an entire-COG hash or ETag immutability. Sentinel-2 L2A imagery from the E84 STAC catalogue with pinned defaults: date window 2023-01-01 to 2024-12-31, `eo:cloud_cover` at most 20, least-cloudy suitable item covering the full AOI, all pages read, stable item-ID tie-break. RGB scaling maps linear reflectance `0..0.2` to `0..255` after applying the asset scale and offset, which are read from metadata and never guessed. The DEM uses 30 m output, `gdalwarp` with bilinear resampling, and GDAL's `PixelIsPoint` handling with no manual half-pixel shift. Numerics are tested, not assumed equivalent to IGN. No-data is never swept under resampling. Surface type `surfaceModel` (DSM), labelled as a lower-resolution fallback that does not meet IGN 1 m or bare-earth requirements.
4. Leave `prepare-sedan-proof.sh` and the `sedan_*` modules unchanged. `prepare_area.py --provider ign-fr` delegates to the existing wrapper; no one-line reimplementation.
5. Add `scripts/geo/tests/test_prepare_area.py` (offline, stdlib only, following the existing `scripts/geo/tests/` suites). Cover: UTM zone edges, hemisphere, and override; polar, antimeridian, and cross-zone rejection; grid arithmetic with no 50 m buffer; schema validation of every file under `providers/`; coverage rejection that names the profile and includes the storage and apron; generated argv for both profiles with the tool runner stubbed; read-only preflight and `--prepare` paths; ledger and receipt; STAC selection across pages; scaling and offset handling; no-data radius and edge policy.
6. Operator evaluations (run after the offline suite passes; no new proof claim):
   - Evaluation A, `ign-fr`: warm reuse of `/mnt/pit2/panopticon/geo-proof/sedan`. Run the read-only preflight, then the warm `--prepare` through the delegate. Then `geoimport import --manifest <root>/sedan-ign-proof.json --out <payload-ign>` and `geoimport report --payload <payload-ign>`. Expected: 3 x 3 chunks, kernels recorded, no unfilled no-data, and the existing proof controls reported. This is a regression check against `WI-R2-01`, not a new 1 m PASS.
   - Evaluation B, `copernicus-glo30`: from an empty `<work>/cop`. Run the STAC query, the tile download or window read, the 30 m DEM and 10 m uint8 RGB warps, the cs2cs KML quadrilateral, `sources.txt`, the manifest with EGM2008 and no stated accuracy, and the receipt and ledger. Then `geoimport import --manifest <work>/cop/sedan-copernicus-proof.json --out <payload-cop>` and `geoimport report --payload <payload-cop>`. Expected: import with no schema or no-data errors, 3 x 3 chunks, bilinear magnification recorded, and no control PASS claimed. Imagery controls: none, because no independent imagery source exists and no self-RGB claim is made.
   - Evaluation B residual table: the five frozen LiDAR primaries are read-only external comparisons. Record apparent differences only, labelled with the datum, surface-versus-bare-earth, resolution, and epoch differences. Never describe a difference as a pure geoid offset. No new 1 m or 4 m PASS.
7. Add a "Adding a provider" subsection to deep dive §8.1 of `docs/roadmap/deep-dives/r-real-world-terrain.md`: required profile fields; the licence check covering full source and derived obligations (attribution notice, derived notice, liability sentence, no endorsement, subsequent users bound, IPR stays with the provider, termination on breach); the rule that each new profile gets one operator import before it is listed; DSM versus bare-earth labelling; Point versus Area sampling; and the 30 m and 10 m magnification notes. Record Evaluations A and B in §8.1 with the artefact names and hashes.

## Files likely to change

- `scripts/geo/prepare_area.py` (new)
- `scripts/geo/providers/ign-fr.json`, `scripts/geo/providers/copernicus-glo30.json` (new)
- `scripts/geo/tests/test_prepare_area.py` (new)
- `docs/roadmap/deep-dives/r-real-world-terrain.md` §8.1 (new subsection and evaluation records)

Not changed: `scripts/geo/prepare-sedan-proof.sh`, `scripts/geo/sedan_*`, `src/*`, `test/*`.

## Acceptance criteria

- [ ] `ign-fr` through `prepare_area.py` reproduces the `WI-R2-01` Sedan inputs: same extent, CRS, resolution, band layout, and chunk grid (1100 m playable, 1170.432 m storage, 3.048 m lattice, 3 x 3 chunks).
- [ ] `copernicus-glo30` prepares the same AOI unattended from an empty work dir, and the import reports no schema or no-data errors and no control PASS.
- [ ] A centre whose storage extent or apron falls outside a profile's coverage fails in preflight, before any download, with a message naming the profile.
- [ ] Other centres and sizes derive the chunk grid through the import contract with no 50 m buffer. Polar, antimeridian, and cross-zone inputs are rejected with a named error.
- [ ] Profile parsing rejects invalid, non-finite, and untrusted identifiers before any path or network use.
- [ ] `scripts/geo/tests/test_prepare_area.py` passes offline, and the full `scripts/geo/tests` suites pass under both the venv and stdlib interpreters.
- [ ] Deep dive §8.1 holds the provider-addition rules with full licence obligations and both evaluation records. No source, derived, or preview data is committed.

## Validation commands

```sh
python3 -m unittest discover -s scripts/geo/tests -p "test_prepare_area.py"
python3 -m unittest discover -s scripts/geo/tests
python3 scripts/geo/prepare_area.py --help
python3 scripts/geo/prepare_area.py --provider ign-fr --center 49.702,4.947 --size 1100 --name sedan-proof "<work dir>/ign"
python3 scripts/geo/prepare_area.py --provider ign-fr --center 49.702,4.947 --size 1100 --name sedan-proof --prepare "<work dir>/ign"
python3 scripts/geo/prepare_area.py --provider copernicus-glo30 --center 49.702,4.947 --size 1100 --name sedan-proof --prepare "<work dir>/cop"
geoimport import --manifest "<work dir>/cop/sedan-copernicus-proof.json" --out "<work dir>/payload-cop"
geoimport report --payload "<work dir>/payload-cop"
```

The first command is offline. The preflight example writes nothing. The `--prepare` examples are operator-only and never run in CI.

## Out of scope / guardrails

- Importer, manifest schema, and GeoImport C# unchanged. Profiles only produce inputs the importer already accepts.
- No further national profiles (Netherlands AHN, German state DGM1, Spain CNIG, swisstopo, Belgian regions, UK Environment Agency) and no department lookup beyond the pinned Sedan products.
- No cross-border mosaics and no vertical datum offsets between providers (W3a owns the TAW-to-NGF-IGN69 offset).
- No providers that need accounts, API keys, RequesterPays buckets, or interactive licence acceptance. No Google content of any kind (see §2).
- No source, derived, or preview data committed. No preview, native run, 300-frame run, or screenshots. No LiDAR re-freeze. No 4 m guarantee. No self-RGB imagery claim. No new framework or abstraction beyond the driver and two profiles.
- No changes to `prepare-sedan-proof.sh`, `scripts/geo/sedan_*`, `src/*`, or `test/*`.

## On completion

- Keep Status `Ready` until implementation starts. Set `Done` or `Blocked` only with the Architect's direction.
- Tick the R2 provider profiles item in the checklist and update the README Track R progress rows only on `Done`.
- Add a `Result:` line only on completion.
