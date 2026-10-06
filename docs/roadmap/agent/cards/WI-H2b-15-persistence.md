# Task Card `WI-H2b-15`: Version saves, checkpoints, and recordings

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-15` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-11`, `WI-H2b-14` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Make saves, engine checkpoints, and run recordings carry an explicit unit version with a documented read, migrate, or reject outcome.

## Context to read (and only this)

- `src/Panopticon.Simulation/Data/Sav/SaveFile.cs`.
- `src/Panopticon.Simulation/Recording/EngineCheckpoint.cs:52-104`: `FormatVersion = 1`.
- `src/Panopticon.Simulation/Recording/` stream, snapshot, and artifact writers.
- Card 02 compatibility matrix.

## Steps

1. Bump format versions per card 02 and implement its policy for old versions.
2. Original Generals `.sav` files convert on load and invert on save if card 02 keeps that path.
3. Test old-version handling and new-version round trips.

## Files likely to change

- Files named by card 02 under `src/Panopticon.Simulation/Data/Sav/` and `Recording/`.
- Matching tests under `test/Panopticon.Simulation.Tests/SaveLoad/` and `Recording/`.

## Acceptance criteria

- [ ] No old-version file is silently read as meters.
- [ ] New checkpoint restore round trips pass.
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

No recording-mode or cadence changes. Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-15 in the checklist, and append the result to `.exchange`.
