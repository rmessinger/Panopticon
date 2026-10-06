# Task Card `WI-H2b-13`: Convert cameras, rendering distances, and audio attenuation

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-13` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-06`, `WI-H2b-08`, `WI-H2b-10` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Keep camera framing, far planes, fog, shadow distances, terrain LOD, and audio attenuation visually and audibly equivalent in meters.

## Context to read (and only this)

- `src/Panopticon.Simulation/Graphics/Cameras/`: controllers and constraints.
- Render and audio consumers listed by card 01, including shader constants.

## Steps

1. Convert the card 02 list of render and audio length constants.
2. Capture before/after screenshots per `docs/screenshots.md`; framing must match.
3. Add unit tests for camera constraint values and attenuation ranges.

## Files likely to change

- Files named by card 02 under `src/Panopticon.Simulation/Graphics/`, `src/Panopticon.Rendering/`, and `src/Panopticon.Simulation/Audio/`.
- Matching tests.

## Acceptance criteria

- [ ] Default camera framing on a reference map matches the pre-cutover capture.
- [ ] Shadow and fog cutoffs are unchanged on screen.
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

No graphics-quality changes owned by Track G. Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-13 in the checklist, and append the result to `.exchange`.
