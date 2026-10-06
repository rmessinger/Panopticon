# Task Card `WI-H2b-07`: Convert terrain spacing and elevation

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-07` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-03` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Make heightmap horizontal spacing and elevations metric while raw map data stays in source units.

## Context to read (and only this)

- `src/Panopticon.Simulation/Terrain/HeightMap.cs`: `HorizontalScale = 10`, `_verticalScale`, `GetHeight`, `LowerHeight`, `ConvertWorldCoordinates`.
- `src/Panopticon.Simulation/Data/Map/HeightMapData.cs`: raw elevations.
- `src/Panopticon.Tools.WorldBuilder.Core/HeightMapEdit.cs`: inverse write path.

## Steps

1. Express horizontal spacing and vertical scale through the conversion helper per card 02; keep sample indices integral.
2. Convert inverse edits (`LowerHeight`, WorldBuilder edits) back to source units before writing raw elevations.
3. Test cell spacing `3.048` m, elevation round trip, negative and border coordinates, and large-map sampling.

## Files likely to change

- `src/Panopticon.Simulation/Terrain/HeightMap.cs`.
- `src/Panopticon.Tools.WorldBuilder.Core/HeightMapEdit.cs`.
- Terrain tests listed by card 02.

## Acceptance criteria

- [ ] `HeightMapData` stores unchanged source values; a load/edit/save round trip is byte-identical for untouched cells.
- [ ] World-space terrain queries match legacy queries times `s`.
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

No blend tiles, object placement, or pathfinding (cards 08 and 12). Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-7 in the checklist, and append the result to `.exchange`.
