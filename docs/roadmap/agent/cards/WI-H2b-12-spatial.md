# Task Card `WI-H2b-12`: Convert navigation, partitioning, and script distances

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-12` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-08`, `WI-H2b-11` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Convert pathfinding cell size, partition cell sizing, and script distance parameters to metric.

## Context to read (and only this)

- `src/Panopticon.Simulation/Logic/AI/AIPathfind.cs:5`: `PathfindCellSizeF = 10.0f`.
- `src/Panopticon.Simulation/Logic/AI/Pathfinder.cs` and `src/Panopticon.Simulation/Navigation/`.
- `src/Panopticon.Simulation/Logic/PartitionCellManager.cs:23-34,216`.
- Script actions and conditions with distance arguments listed by card 01.

## Steps

1. Derive pathfind cell size from the terrain cell size so grid alignment is unchanged.
2. Convert script distance arguments at script load per card 02.
3. Test path cell indexing, partition lookups, and a distance-condition script.

## Files likely to change

- Files named by card 02.
- Matching tests under `test/Panopticon.Simulation.Tests/Navigation/` and `Scripting/`.

## Acceptance criteria

- [ ] Pathfind and terrain grids stay aligned one-to-one.
- [ ] A legacy path matches the old path times `s` within tolerance.
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

No pathfinding algorithm changes. Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-12 in the checklist, and append the result to `.exchange`.
