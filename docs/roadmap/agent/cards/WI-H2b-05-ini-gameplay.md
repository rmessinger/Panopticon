# Task Card `WI-H2b-05`: Convert gameplay INI lengths and rates at parse time

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-05` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-03`, `WI-H2b-04` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Normalize gameplay-affecting INI distances, speeds, and accelerations to metric where they enter the engine.

## Context to read (and only this)

- `src/Panopticon.Simulation/Data/Ini/IniParser.cs:466-484`: per-second, velocity, and acceleration converters.
- Card 01 inventory rows for weapon, locomotor, object, geometry, and module fields parsed as plain floats.
- Card 02 field list and parser-helper decision.

## Steps

1. Add the typed distance parse helpers chosen by card 02; leave `ParseFloat` and `ParseVector3` unit-agnostic.
2. Switch only the inventoried gameplay fields to the typed helpers, including `ParseVelocityToLogicFrames` and `ParseAccelerationToLogicFrames` call sites that carry lengths.
3. Add parse tests per field family with converted expectations; include BFME variants listed by card 01.

## Files likely to change

- `src/Panopticon.Simulation/Data/Ini/IniParser.cs`.
- Gameplay definition files listed by card 02.
- Matching tests under `test/Panopticon.Simulation.Tests/Data/Ini/`.

## Acceptance criteria

- [ ] Every inventoried gameplay length has exactly one conversion owner.
- [ ] Angular, time, ratio, and dimensionless fields are unchanged.
- [ ] Card 04's double-conversion guard still passes.
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

No presentation fields, no `GameData` globals, no physics constants (cards 06 and 11). Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-5 in the checklist, and append the result to `.exchange`.
