# Task Card `WI-H2b-06`: Convert presentation INI lengths and global constants

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-06` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-05` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Normalize `GameData` globals and presentation INI lengths (FX, particles, camera, audio ranges) to metric.

## Context to read (and only this)

- `src/Panopticon.Simulation/GameData.cs:149-163`: `CameraHeight`, `MaxCameraHeight`, `MinCameraHeight`, `Gravity`, `PartitionCellSize`.
- Card 01 inventory rows for FX, particle, audio, and camera INI fields.

## Steps

1. Switch the card 02 list of global and presentation fields to the typed helpers.
2. Verify `Gravity` converts the source value rather than substituting a terrestrial constant.
3. Add parse tests with converted expectations.

## Files likely to change

- `src/Panopticon.Simulation/GameData.cs`.
- Presentation definition files listed by card 02.
- Matching tests.

## Acceptance criteria

- [ ] Every inventoried presentation or global length has one conversion owner.
- [ ] Gravity equals the legacy value times `s`.
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

No renderer or camera consumer changes (card 13). Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-6 in the checklist, and append the result to `.exchange`.
