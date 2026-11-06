# Task Card `WI-H2-02`: Add engine-owned map interchange types

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2-02` |
| Roadmap phase | [Track H · H2](../../track-h-storage-formats.md): interchange abstraction |
| Status | `Done` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-H2-01` |
| Diagrams | none |

## Goal

Implement the engine-owned `MapInterchange` document model from the [H2 map interchange design](../../deep-dives/h1-h2-map-storage.md): the root type and its components, the unit and frame declarations for every quantity, model-level validation, and the mask bit definitions. The types live in a new `src/Panopticon.Simulation/Content/Importing/Maps/` namespace folder mirroring the H2a `Content/Importing/Units/` pattern and carry zero `MapFile` dependency.

## Context to read (and only this)

- [H2 map interchange design](../../deep-dives/h1-h2-map-storage.md): Interchange document model, Units, frames, and quantities, Chunk geometry, ordering, and nodata, Masks and palette semantics, and Validation and error behavior.
- `src/Panopticon.Simulation/Content/Importing/Units/` as the namespace folder and contract-test pattern.
- `src/Panopticon.Geospatial/Generation/TerrainMaskFlags.cs` and the B4b mask writer `src/Panopticon.Geospatial/Payload/GeneratedMapPayloadWriter.cs` (defined bits in `src/Panopticon.Geospatial/Payload/Storage/PayloadChunkCodec.cs`) as the mask bit grounding.

## Steps

1. Create `src/Panopticon.Simulation/Content/Importing/Maps/` with the `MapInterchange` root, the components from the deep dive model table, and an interchange schema version independent of source versions.
2. Declare unit and frame per quantity kind as contract properties; `GameUnit` assumes no physical unit.
3. Copy mask bit positions from the B4b payload writer and fix the known flag set.
4. Implement model-level validation with typed diagnostics and no partial publication: exact chunk inventory, finite heights, seam and apron equality, palette bounds, known mask flags, imagery consistency, unique stable IDs, retained-section versions.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Maps/` (new namespace folder).
- A new test file under `test/Panopticon.Simulation.Tests`.

## Acceptance criteria

- [x] The model matches the deep dive component table, including `Provenance`, `ImportReport`, and `RetainedSection`, and carries an interchange schema version independent of the source payload and legacy section versions.
- [x] Every coordinate, height, spacing, and scale value declares its unit and frame per the quantity table; source values are preserved as read.
- [x] Model-level validation enforces the deep dive check list and returns typed diagnostics without publishing partial content.
- [x] Mask bit positions equal the B4b writer bits, and any bit outside the known set fails validation.
- [x] No file under the Maps folder references the `MapFile` type; `grep -rn "MapFile" src/Panopticon.Simulation/Content/Importing/Maps/` returns nothing.

## Validation commands

```sh
dotnet build src/Panopticon.Simulation -v minimal
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~MapInterchange"
# Before PR: MCP test run per docs/roadmap/agent guidance in AGENTS.md.
```

## Out of scope / guardrails

No `MapFile` dependency, no reader or adapter, no runtime materialization, and no H3 serialization. The seam and adapter belong to WI-H2-03; residency to WI-H2-04 and B4a.

## On completion

- Result: 2026-11-06 done: added 22 engine-owned contract types under `src/Panopticon.Simulation/Content/Importing/Maps/` (unit- and frame-explicit `MapQuantity`, interchange schema version independent of source payload and legacy section versions, `MapMaskFlags` bits equal to the B4b writer, 29-name section registry, retained-section byte integrity with SHA-256) plus typed model validation with 17 stable diagnostic codes and 22 unit tests (one positive, one per diagnostic code with two documented co-occurring seam/apron cases, one unit-preservation). Validation: `dotnet build` 0 errors with only the 11 pre-existing warnings, focused `dotnet test --filter FullyQualifiedName~MapInterchange` 22/22 passed, MCP run `20261106T180611Z-18a6fe71` on `track-h-h2-2-interchange-types` 4778 passed / 10 known failures (TF-01..TF-10) / 0 new failures. Follow-up: WI-H2-03 reader seam and legacy adapter.
- Set Status above to `Done` or `Blocked`.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
