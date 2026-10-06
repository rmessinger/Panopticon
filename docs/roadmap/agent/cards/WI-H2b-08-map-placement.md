# Task Card `WI-H2b-08`: Convert map placement and WorldBuilder adapters

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-08` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-07` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Convert placed objects, waypoints, polygon triggers, named cameras, and borders at map load, inverting on WorldBuilder save.

## Context to read (and only this)

- `src/Panopticon.Simulation/Data/Map/`: `WaypointPath`, `NamedCamera`, `CameraAnimationFrame`, object list and trigger types named by card 01.
- WorldBuilder save paths named by card 01.

## Steps

1. Convert map-space positions where card 02 places the boundary; keep `MapFile` readers source-space.
2. Invert on WorldBuilder write.
3. Test placement, trigger containment, and map round trips.

## Files likely to change

- Files named by card 02 under `src/Panopticon.Simulation/` and `src/Panopticon.Tools.WorldBuilder.Core/`.
- Matching tests.

## Acceptance criteria

- [ ] A legacy map loads with positions equal to source times `s`; a save round trip is byte-identical.
- [ ] Trigger and waypoint lookups agree with terrain from card 07.
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

No scripting distance parameters (card 12). Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-8 in the checklist, and append the result to `.exchange`.
