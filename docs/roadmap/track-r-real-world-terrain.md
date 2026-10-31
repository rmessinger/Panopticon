# Track R: Real-World Terrain

> Part of the [Large-Scale RTS Roadmap](README.md). This track generates editable, playable
> Panopticon maps from operator-supplied real-world geospatial data: Google Earth KML/KMZ plus
> georeferenced elevation and imagery rasters.
>
> Architecture context: see [`agent/current-state.md`](agent/current-state.md).
>
> Audience: human overview of the real-world terrain track.

## Track ordering rationale

The work started as Track B phase B4b and moved here on 2026-10-31 because it is a content and
authoring pipeline, not scale work, and its scope is expected to grow beyond one importer (see the
[track Decision](open-questions.md)). Card IDs (`WI-B4b-NN`), sub-phase labels (`B4b-1` to
`B4b-9`), the deep dive, and the implementation plan keep their B4b names.

R1 and R2 need neither B4a nor Track H: they produce a standalone chunked payload and a dev-only
preview on a legacy `MapFile`. R3 writes the H3 package through the H2 interchange once B4a-5 and
H3 exist, R4 adds the WorldBuilder workflow on H6, and R5 proves one operator-supplied area end to
end. Source boundary, coordinate contract, and generation rules live in the
[deep dive](deep-dives/b4b-geospatial-map-generation.md).

New source types or generation stages (for example other vector sources, point clouds,
bathymetry, or scanned historical maps) take the next phase number.

> The Milestone column states the demonstrable outcome that marks each phase done. Granular steps
> live in [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| R1. Geospatial import core | Build the CPU-only `Panopticon.Geospatial` project: manifest and limits, interchange values, KML/KMZ and GeoTIFF readers, Transverse Mercator and local map grid, terrain, imagery, and material chunks, feature clipping and mapping, passability and buildability masks (sub-phases `B4b-1` to `B4b-6`). | Synthetic fixtures read, project within 1 mm, and generate bit-identical shared chunk edges and identical chunk hashes on repeated runs. | In progress | — |
| R2. Standalone payload and dev preview | Run the pipeline into a transactional standalone payload with a `GeoImport` CLI and control-point validation, then convert a payload into a resident legacy `MapFile` for play testing (sub-phases `B4b-6a`, `B4b-6b`). | A synthetic import passes its control points, and the generated map loads in Generals and runs 300 logic frames. | In progress | R1 |
| R3. Map package output | Populate the H2 map interchange and write chunked terrain, generated assets, vector-derived layers, provenance, and diagnostics through H3 (sub-phase `B4b-7`). | A generated map writes and reloads as an H3 package with its provenance and layers intact. | Not started | R2, B4a, H3 |
| R4. WorldBuilder import workflow | Add transactional import, preview, cancellation, report display, and edit/save/reload in WorldBuilder on H6 (sub-phase `B4b-8`). | An import is one undoable transaction, cancellation leaves the session unchanged, and the result saves and reloads. | Not started | R3, H6 |
| R5. Validation and scale proof | Package and engine round-trips, one operator-supplied control-point proof, and B1 measurements (sub-phase `B4b-9`). | One operator-supplied area matches its control points within declared tolerances, round-trips through WorldBuilder, and plays; B1 records import time and peak memory. | Not started | R4 |

## Phase resources

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| R1 | [Geospatial map generation](deep-dives/b4b-geospatial-map-generation.md) | [§10](deep-dives/b4b-geospatial-map-generation.md#10-architecture-diagrams) | `WI-B4b-01..17` ([plan](implementation/track-b-b4b-geospatial.md)) |
| R2 | [§6.1](deep-dives/b4b-geospatial-map-generation.md#61-standalone-payload-and-dev-preview) | [§10.3, §10.4](deep-dives/b4b-geospatial-map-generation.md#10-architecture-diagrams) | `WI-B4b-18..24` ([plan](implementation/track-b-b4b-geospatial.md)) |
| R3 | [§6](deep-dives/b4b-geospatial-map-generation.md#6-h2h3-and-worldbuilder-boundary) | [§10.1](deep-dives/b4b-geospatial-map-generation.md#10-architecture-diagrams) | none until H2/H3 and B4a-5 |
| R4 | [§6](deep-dives/b4b-geospatial-map-generation.md#6-h2h3-and-worldbuilder-boundary) | [§10.2](deep-dives/b4b-geospatial-map-generation.md#10-architecture-diagrams) | none until H6 |
| R5 | [§8](deep-dives/b4b-geospatial-map-generation.md#8-determinism-and-validation) | — | none until R4 |

## Cross-track dependencies

- R3 consumes B4a-5's runtime chunk model and H3's versioned map container through the H2
  interchange; R4 builds on H6's WorldBuilder read/write integration. The importer targets the
  interchange boundary instead of either storage implementation.
- R5 records its import time and peak memory through [B1](track-b-scale.md).
- [Track W](track-w-western-front.md) W3a (Sedan 1870) and W3b (Somme sector) consume the R2
  payload (`WI-B4b-18`) and R1 types (`WI-B4b-06`, `WI-B4b-16`); W3a owns the topology builder.
