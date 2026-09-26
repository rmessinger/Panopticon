# Track B · B4b: Geospatial map generation

> Audience: human overview above the divider; agent-context detail below it.

Implements B4b-1 through B4b-6b of [Track B](../track-b-scale.md). Design source of truth:
[deep-dives/b4b-geospatial-map-generation.md](../deep-dives/b4b-geospatial-map-generation.md),
including its [architecture diagrams](../deep-dives/b4b-geospatial-map-generation.md#10-architecture-diagrams).

## Scope

Build the CPU-only `Panopticon.Geospatial` pipeline from operator-supplied KML/KMZ and GeoTIFF
sources to a standalone chunked payload, plus a dev-only preview that converts a payload into a
playable legacy `MapFile`. B4b-7 (H2 interchange), B4b-8 (WorldBuilder import), and B4b-9
(operator proof) stay blocked on B4a and H.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| B4b-1 | [`WI-B4b-01`](../agent/cards/WI-B4b-01-scaffold-geospatial-project.md) | Projects, diagnostics, limits. |
| B4b-1 | [`WI-B4b-02`](../agent/cards/WI-B4b-02-import-manifest.md) | Manifest schema and loader. |
| B4b-2 | [`WI-B4b-03`](../agent/cards/WI-B4b-03-interchange-values.md) | Interchange values and interfaces. |
| B4b-4 | [`WI-B4b-04`](../agent/cards/WI-B4b-04-transverse-mercator.md) | Krüger Transverse Mercator. |
| B4b-4 | [`WI-B4b-05`](../agent/cards/WI-B4b-05-coordinate-transformer.md) | `ICoordinateTransformer` for supported CRS. |
| B4b-4 | [`WI-B4b-06`](../agent/cards/WI-B4b-06-local-map-grid.md) | `LocalMapGrid`, chunk rounding, scale-error check. |
| B4b-3 | [`WI-B4b-07`](../agent/cards/WI-B4b-07-kml-reader.md) | KML reader. |
| B4b-3 | [`WI-B4b-08`](../agent/cards/WI-B4b-08-kmz-archive.md) | KMZ archive reader. |
| B4b-3 | [`WI-B4b-09`](../agent/cards/WI-B4b-09-geotiff-fixtures.md) | Synthetic GeoTIFF fixtures. |
| B4b-3 | [`WI-B4b-10`](../agent/cards/WI-B4b-10-geotiff-reader.md) | GeoTIFF reader. |
| B4b-5 | [`WI-B4b-11`](../agent/cards/WI-B4b-11-raster-sampling.md) | Sampling kernels, no-data and edge policies. |
| B4b-5 | [`WI-B4b-12`](../agent/cards/WI-B4b-12-terrain-chunks.md) | Terrain chunks. |
| B4b-5 | [`WI-B4b-13`](../agent/cards/WI-B4b-13-imagery-chunks.md) | Imagery chunks. |
| B4b-5 | [`WI-B4b-14`](../agent/cards/WI-B4b-14-material-layer.md) | Material layer. |
| B4b-6 | [`WI-B4b-15`](../agent/cards/WI-B4b-15-feature-clipping.md) | Feature transform and clipping. |
| B4b-6 | [`WI-B4b-16`](../agent/cards/WI-B4b-16-feature-mapping.md) | Feature mapping rules. |
| B4b-6 | [`WI-B4b-17`](../agent/cards/WI-B4b-17-terrain-masks.md) | Passability and buildability masks. |
| B4b-6a | [`WI-B4b-18`](../agent/cards/WI-B4b-18-import-pipeline-payload.md) | Import pipeline and payload. |
| B4b-6a | [`WI-B4b-19`](../agent/cards/WI-B4b-19-geoimport-cli.md) | `Panopticon.Tools.GeoImport` CLI. |
| B4b-6a | [`WI-B4b-20`](../agent/cards/WI-B4b-20-control-point-validation.md) | Control-point validation. |
| B4b-6b | [`WI-B4b-21`](../agent/cards/WI-B4b-21-audit-playable-mapfile.md) | Minimum playable `MapFile` audit. |
| B4b-6b | [`WI-B4b-22`](../agent/cards/WI-B4b-22-mapfile-defaults.md) | Legacy `MapFile` default factories. |
| B4b-6b | [`WI-B4b-23`](../agent/cards/WI-B4b-23-payload-to-mapfile.md) | `Panopticon.Geospatial.Preview`. |
| B4b-6b | [`WI-B4b-24`](../agent/cards/WI-B4b-24-preview-evidence.md) | `preview` verb and game-backed evidence. |

Related design cards: [`WI-B4a-01`](../agent/cards/WI-B4a-01-chunk-contract.md) (B4a-5 chunk
contract), [`WI-H1-01`](../agent/cards/WI-H1-01-map-format-audit.md),
[`WI-H1-02`](../agent/cards/WI-H1-02-format-decision-options.md), and
[`WI-H2-01`](../agent/cards/WI-H2-01-map-interchange-design.md).

---

## Agent context

## Dependency order

| Card | Prerequisites |
|---|---|
| 01, 21 | none |
| 02, 03, 04 | 01 |
| 05 | 03, 04 |
| 06 | 05 |
| 07, 09, 11 | 03 |
| 08 | 07 |
| 10 | 03, 09 |
| 12 | 06, 11 |
| 13 | 12 |
| 15 | 06, 07 |
| 14, 16 | 12, 15 |
| 17 | 12, 16 |
| 18 | 02, 08, 10, 13, 14, 16, 17 |
| 19, 20 | 18 |
| 22 | 21 |
| 23 | 18, 22 |
| 24 | 20, 23 |

`WI-B4b-21` and `WI-B4b-22` touch only `Panopticon.Simulation` and can run in parallel with the
geospatial chain.

## Layout

| Folder | Contents |
|---|---|
| `src/Panopticon.Geospatial/` | diagnostics, limits |
| `.../Manifest/` | manifest records, `ManifestLoader` |
| `.../Interchange/` | CRS ids, affine, rasters, features, provenance, interfaces |
| `.../Projection/` | Transverse Mercator, transformer |
| `.../Grid/` | `ChunkLayout`, `LocalMapGrid` |
| `.../Readers/` | KML, KMZ, GeoTIFF |
| `.../Sampling/` | `RasterSampler` |
| `.../Generation/` | terrain, imagery, material, mask generators |
| `.../Features/` | clipping, mapping |
| `.../Pipeline/`, `.../Payload/`, `.../Validation/` | importer, payload I/O, control points |
| `src/Panopticon.Geospatial.Preview/` | payload to legacy `MapFile` |
| `src/Panopticon.Tools.GeoImport/` | CLI |
| `test/Panopticon.Geospatial.Tests/` | all B4b tests except `MapFile` factory tests |

## Invariants

- `Panopticon.Geospatial` references no Panopticon project; only `.Preview` references `Panopticon.Simulation`.
- No test downloads data; GeoTIFF fixtures are generated at test time.
- Every source raster is sampled exactly once per output sample (single inverse warp).
- Chunk traversal is row-major from the southwest; shared chunk edges are bit-identical.
- Failures surface as typed `GeoDiagnostic` codes, never parsed exception messages.

## Agent context: minimum playable map

Written by `WI-B4b-21`.
