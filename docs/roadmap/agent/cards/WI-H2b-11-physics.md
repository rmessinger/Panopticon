# Task Card `WI-H2b-11`: Convert physics and locomotion constants

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-11` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-05`, `WI-H2b-07`, `WI-H2b-09` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Convert hardcoded length thresholds in physics and locomotion so behavior matches the legacy run scaled by `s`.

## Context to read (and only this)

- `src/Panopticon.Simulation/Logic/Object/Locomotor.cs:40,301`: `DonutDistance` and path-distance thresholds.
- `PhysicsBehavior` and `src/Panopticon.Physics/` constants listed by card 01.

## Steps

1. Replace the card 02 list of length literals with helper-derived constants; scale squared thresholds by `s²`.
2. Add targeted tests comparing scaled legacy trajectories within card 02 tolerances.

## Files likely to change

- Files named by card 02 under `src/Panopticon.Simulation/Logic/Object/` and `src/Panopticon.Physics/`.
- Matching tests.

## Acceptance criteria

- [ ] No remaining feet-valued length literal in the audited physics scope.
- [ ] Fall, bounce, and locomotion tests match scaled legacy expectations.
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

No mass or force reinterpretation without card 02 evidence. Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-11 in the checklist, and append the result to `.exchange`.
