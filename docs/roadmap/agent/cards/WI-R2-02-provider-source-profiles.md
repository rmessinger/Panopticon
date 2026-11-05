# Task Card `WI-R2-02`: Provider source profiles for data preparation

| Field | Value |
|---|---|
| Card ID | `WI-R2-02` |
| Roadmap phase | [Track R · R2](../../track-r-real-world-terrain.md): operator data preparation beyond France |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `M` |
| Prerequisites | `WI-R2-01` (the working IGN preparation script) |
| Diagrams | [§10.4 standalone import and dev preview flow](../../deep-dives/r-real-world-terrain.md#104-standalone-import-and-dev-preview-flow) |

## Goal

Replace the France-only `scripts/geo/prepare-sedan-proof.sh` with a provider-neutral preparation script driven by one small source profile per data provider, so any European area can be prepared from the command line by choosing a provider, a centre, and a size. The importer is unchanged: every profile ends in the same EPSG:4326 KML plus WGS84 UTM GeoTIFF inputs.

## Context to read (and only this)

- `docs/roadmap/agent/cards/WI-R2-01-sedan-operator-proof.md` (steps 1 and 7) and `scripts/geo/prepare-sedan-proof.sh`.
- `docs/roadmap/deep-dives/r-real-world-terrain.md` §2 (source boundary), §4 (coordinates, datum, tolerances), §8.1.
- `docs/roadmap/open-questions.md` B4b pipeline decision (accepted CRS, one DEM, one optional imagery raster).
- `scripts/tests/test_annotate_capture_gif.py` (Python test style).

## Design

| Item | Choice |
|---|---|
| Driver | `scripts/geo/prepare_area.py` (Python 3 standard library only) calling the GDAL, `curl`, and `7z` command-line tools |
| Invocation | `prepare_area.py --provider <id> --center <lat>,<lon> --size <meters> --name <area> <work dir>` |
| Profiles | `scripts/geo/providers/<id>.json`, one per provider; no provider logic in the driver beyond named download kinds |
| Target CRS | WGS84 UTM zone from the centre longitude (`floor((lon + 180) / 6) + 1`, EPSG:326zz north); `--utm-zone` overrides it to keep a multi-area project in one zone |
| Outputs | `<name>-dem.tif`, `<name>-imagery.tif` (when the profile has imagery), `<name>.kml`, `sources.txt`, `control-points.json` (when the profile has benchmarks) |
| Idempotence | Downloads resume with `curl -fL -C -`; existing extracted tiles and outputs are reused unless `--force` |

Profile fields:

| Field | Meaning |
|---|---|
| `id`, `label`, `coverage` | Provider name and WGS84 bounding box; the driver rejects a centre outside it |
| `elevation.download` | Download kind (`listing`, `tile-grid`, `stac`) and its parameters (base URL, archive or tile name pattern, region code rule) |
| `elevation.sourceCrs`, `verticalDatum`, `verticalAccuracyMeters` | Source projection for `gdalbuildvrt -a_srs`, datum label copied into `sources.txt` for the manifest, stated accuracy |
| `elevation.resampling` | `gdalwarp -r` value (default `bilinear`) |
| `imagery` | Optional; same download fields, band selection, `-r average` |
| `benchmarks` | Optional; WFS URL, layer, altitude field, datum |
| `licence`, `attribution` | Text copied into `sources.txt` and printed for the manifest's source attribution |

## Steps

1. Add `prepare_area.py` with the design above. Port the logic of `prepare-sedan-proof.sh` (centre transform, ±size/2 extent, +50 m warp window, VRT mosaic, `gdalwarp` to classic single-image GeoTIFF, `ogr2ogr -f KML` extent and placemarks, `sources.txt`).
2. Add `providers/ign-fr.json` holding exactly the IGN RGE ALTI 1 m, BD ORTHO 20 cm, and levelling-benchmark WFS settings from `WI-R2-01`, with the department code derived from the centre.
3. Add `providers/copernicus-glo30.json` as the Europe-wide fallback: Copernicus DEM GLO-30 tiles from the public unauthenticated mirror (1° tile-grid naming), datum `EGM2008`, and Sentinel-2 L2A true-colour imagery from a public STAC catalogue (least-cloudy scene within a date window set in the profile, bands B04/B03/B02 scaled to 8-bit). No benchmarks. Record the mirror and catalogue URLs and their licence text in the profile.
4. Replace `prepare-sedan-proof.sh` with a one-line wrapper that calls `prepare_area.py --provider ign-fr --center 49.702,4.947 --size 1100 --name sedan-proof`, so `WI-R2-01` commands keep working.
5. Add `scripts/tests/test_prepare_area.py` (offline): UTM zone selection including zone edges and the override, extent and warp-window arithmetic, schema validation of every file under `providers/`, coverage rejection, and the generated GDAL command lines for both profiles with subprocess stubbed.
6. Operator run: prepare the Sedan proof area with `ign-fr` and the same area with `copernicus-glo30`; import both with `GeoImport import` and `report`. Record in deep dive §8.1 the benchmark residuals for both DEMs and the `NGF-IGN69` versus `EGM2008` offset seen at the benchmarks.
7. Add a short "Adding a provider" subsection to deep dive §8.1: required fields, licence check (open reuse and redistribution, attribution only), and the rule that each new profile gets one operator import before it is listed.

## Files likely to change

- `scripts/geo/prepare_area.py` (new)
- `scripts/geo/providers/ign-fr.json`, `scripts/geo/providers/copernicus-glo30.json` (new)
- `scripts/geo/prepare-sedan-proof.sh` (becomes a wrapper)
- `scripts/tests/test_prepare_area.py` (new)
- `docs/roadmap/deep-dives/r-real-world-terrain.md` §8.1

## Acceptance criteria

- [ ] `prepare_area.py` prepares the Sedan area with `ign-fr` and produces files equivalent to `WI-R2-01`'s script (same extent, CRS, resolution, band layout).
- [ ] The same area prepares unattended with `copernicus-glo30` and imports with no schema or no-data errors.
- [ ] A centre outside a profile's coverage fails before any download with a message naming the profile.
- [ ] `test_prepare_area.py` passes offline.
- [ ] §8.1 records both runs and the provider-addition rules; no source or derived data is committed.

## Validation commands

```sh
python3 -m unittest discover -s scripts/tests -p "test_prepare_area.py"
python3 scripts/geo/prepare_area.py --provider ign-fr --center 49.702,4.947 --size 1100 --name sedan-proof "<work dir>/ign"
python3 scripts/geo/prepare_area.py --provider copernicus-glo30 --center 49.702,4.947 --size 1100 --name sedan-proof "<work dir>/cop"
```

## Out of scope / guardrails

- Importer, manifest schema, or GeoImport changes; profiles only produce inputs the importer already accepts.
- Further national profiles (Netherlands AHN, German state DGM1, Spain CNIG, swisstopo, Belgian regions, UK Environment Agency): one card per provider when an area needs it.
- Cross-border mosaics and vertical datum offsets between providers (W3a owns the TAW-to-NGF-IGN69 offset).
- Providers that need accounts, API keys, or interactive licence acceptance.
- Google content of any kind (see §2).

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick the R2 provider profiles item in the checklist and update the README Track R progress rows.
- Append a result line to `.exchange`.
