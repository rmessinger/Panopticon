# Task Card `WI-H2b-10`: Remove meters-to-feet expansion from metric-native importers

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-10` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-09` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Make glTF and NIF meter coordinates import as meters, preserving axis conversion and source-specific sizing.

## Context to read (and only this)

- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfTransformConverter.cs`: `CreateBasis(unitsPerMeter)`.
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfImportOptions.cs`: default units-per-meter.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifTransformConverter.cs`: `MetersPerFoot`, `FeetPerMeter`, `SourceToEngine`.
- NIF particle converter and any SC2/M3 importer named by card 01.

## Steps

1. Set the glTF default to `1` unit per meter and remove `FeetPerMeter` from the NIF basis.
2. Keep any presentation sizing factor recorded by card 02 separate from unit conversion.
3. Update C4 and EE2 import tests: a 1 m fixture measures 1 m; the EE2 druid stays upright with feet on the ground.

## Files likely to change

- Files under `src/Panopticon.Simulation/Content/Importing/Gltf/` and `Nif/` named by card 02.
- Matching tests.

## Acceptance criteria

- [ ] No importer contains `3.28084` or a local `MetersPerFoot`.
- [ ] EE2 and Generals models keep their current relative on-screen size.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

Plus the targeted tests pinned by `WI-H2b-02`.

## Out of scope / guardrails

No change to rotation conventions or animation sampling. Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-10 in the checklist, and append the result to `.exchange`.
