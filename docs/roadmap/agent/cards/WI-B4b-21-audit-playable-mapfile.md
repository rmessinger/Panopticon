# Task Card `WI-B4b-21`: Audit the minimum playable legacy MapFile

| Field | Value |
|---|---|
| Card ID | `WI-B4b-21` |
| Roadmap phase | [Track R · R2](../../track-r-real-world-terrain.md): B4b-6b dev preview |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Record exactly which `MapFile` sections and fields the engine and `WriteTo` require for a generated map to save, load, and start a skirmish.

## Context to read (and only this)

- `src/Panopticon.Simulation/Data/Map/MapFile.cs` (`CreateEmpty`, `WriteTo`).
- `src/Panopticon.Simulation/Data/Map/HeightMapData.cs`, `BlendTileData.cs`, `WorldInfo.cs`, `SidesList.cs`.
- `src/Panopticon.Simulation/Game.cs` `LoadMap` and `StartSkirmishOrMultiPlayerGame`.
- `src/Panopticon.Tools.WorldBuilder.Core/NewMapFactory.cs`.

## Steps

1. Trace `WriteTo` and every `mapFile.<Section>` read reachable from `LoadMap` and scene construction; list null-tolerant versus required sections.
2. For each required section, record the minimal valid value (versions, texture table entries, blend arrays, passability defaults, sides/players for skirmish, start waypoints).
3. Note the 8-bit versus 16-bit `HeightMapData` version rule and the terrain texture names available in Generals `INI.big`.
4. Write the result as the `Agent context: minimum playable map` section in `docs/roadmap/implementation/track-b-b4b-geospatial.md`, with file:line references.

## Files likely to change

- `docs/roadmap/implementation/track-b-b4b-geospatial.md`

## Acceptance criteria

- [ ] The section lists every section `WriteTo` writes and every section `LoadMap` reads, with required/optional and minimal values.
- [ ] It names at least one skirmish-valid player and start-waypoint configuration.
- [ ] Standard validation passes.

## Validation commands

```sh
git --no-pager diff --check
```

## Out of scope / guardrails

No code changes.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-22`.
