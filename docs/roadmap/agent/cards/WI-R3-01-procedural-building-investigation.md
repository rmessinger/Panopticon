# Task Card `WI-R3-01`: Procedural buildings from footprints (investigation)

| Field | Value |
|---|---|
| Card ID | `WI-R3-01` |
| Roadmap phase | [Track R · R3](../../track-r-real-world-terrain.md): structure representation ahead of map package output |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `M` |
| Prerequisites | `WI-R2-03` (building footprints in the Sedan proof KML) |
| Diagrams | none; the recommendation feeds R3 diagrams |

## Goal

Decide how R3 represents real-world buildings without a bespoke model per building. Prototype route 1 of deep dive §5.4.1 on the Sedan footprints: extrude each footprint to its height, add a roof, texture walls and roofs with CC0 PBR materials, and judge appearance, triangle and draw cost, and authoring effort. End with a written recommendation, not production code.

## Context to read (and only this)

- `docs/roadmap/deep-dives/r-real-world-terrain.md` §5.3, §5.4 (structures and candidate content sources), §8.1.
- `docs/roadmap/agent/cards/WI-R2-03-building-footprint-masks.md` and the Sedan `sources.txt` (BD TOPO edition, kept attributes).
- `src/Panopticon.Tools.TestHarness/GltfRuntimeProofScenario.cs`, `StandaloneModelCapture.cs` (glTF import and capture path).
- `docs/screenshots.md`.

## Questions to answer

1. Which BD TOPO attributes are populated in the Sedan extent (height, roof altitudes, storeys, use), and what share of footprints has a usable height?
2. Which roof rule works from footprint shape alone: flat, hipped from the straight skeleton, or gabled along the longest axis? How many footprints fail the rule (concave, holes, tiny)?
3. Do tiling CC0 materials (ambientCG, Poly Haven brick, plaster, slate, roof tile) with UVs in world meters read correctly at Generals camera distance, and does a per-building colour or material variant hide repetition?
4. Do Kenney or Quaternius kit details (doors, windows, chimneys) placed along walls add enough to justify their style mismatch with realistic materials?
5. Cost at the Sedan proof extent: buildings, triangles, materials, and draw calls, merged per terrain chunk versus one object per building. Frame time on the preview map for both layouts.
6. Which layout fits R3 and H3: merged generated chunk meshes as package assets, or per-building objects with a shared definition and per-instance scale? Note effects on selection, damage, and line of sight.

## Steps

1. From the prepared Sedan KML and `sources.txt`, tabulate attribute coverage and footprint shape statistics.
2. Prototype a generator on a throwaway branch (Python script or a small C# console): footprint plus height to walls and roof, world-meter UVs, glTF 2.0 output per terrain chunk and per building. Download materials and any kit pieces into an operator work dir; commit none.
3. Capture the merged-chunk glTF through the existing glTF import and standalone model capture path.
4. Temporarily spawn the generated meshes at their game positions on the Sedan preview map in a scratch harness scenario, using the payload grid transform; capture an overview and an old-town close-up, and record B1 frame time with buildings off, merged, and per-building.
5. Record each source's licence and attribution requirement as found at download time.
6. Write results and a recommendation into deep dive §5.4.2: answers to the questions, captures, cost table, chosen layout and roof rule, material and kit list with licences, and the R3 cards it implies.

## Files likely to change

- `docs/roadmap/deep-dives/r-real-world-terrain.md` §5.4.2 (results)
- No source changes on `main`; prototype code stays on the investigation branch and is linked from §5.4.2.

## Acceptance criteria

- [ ] §5.4.2 answers all six questions with numbers from the Sedan extent.
- [ ] Captures for merged and per-building layouts are in the screenshot viewer under versioned names.
- [ ] Every material and kit source used is listed with its licence; nothing with an unclear licence is recommended.
- [ ] The recommendation names the layout, roof rule, and follow-up R3 cards.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario GltfRuntimeProof --headless
```

## Out of scope / guardrails

- Production generator code, manifest schema changes, and H3 package output.
- Bespoke landmark models (the château); note them as follow-ups only.
- LoD2 city model import (3D BAG, German state LoD2, swissBUILDINGS3D); a separate source type.
- OpenStreetMap data and non-commercial or unclear licences.
- Committing downloaded materials, kits, IGN data, or generated meshes.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick the R3 structure investigation item in the checklist and update the README Track R progress rows.
- Append a result line to `.exchange`.
