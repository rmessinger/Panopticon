# Task Card `WI-H2b-14`: Version orders, network payloads, and legacy replays

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-14` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-08`, `WI-H2b-12` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Keep original `.rep` playback working by converting legacy order positions at the replay and network boundary.

## Context to read (and only this)

- `src/Panopticon.Simulation/Logic/Orders/OrderArgumentValue.cs:24`: `Position`.
- `src/Panopticon.Simulation/Data/Rep/ReplayFile.cs`, `ReplayChunk.cs`, `src/Panopticon.Simulation/Network/ReplayConnection.cs`.
- Card 02 compatibility matrix rows for replays and network.

## Steps

1. Convert legacy order positions on replay read and invert on legacy write.
2. Version any Panopticon-owned network or replay payload per card 02; unversioned payloads are never read as meters.
3. Test a recorded `.rep` chunk round trip and position conversion.

## Files likely to change

- Files named by card 02 under `src/Panopticon.Simulation/Data/Rep/`, `Network/`, and `Logic/Orders/`.
- Matching tests.

## Acceptance criteria

- [ ] ReplaySketch plays an original `.rep` with order positions at source times `s`.
- [ ] A legacy replay write round trip is byte-identical.
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

No ReplaySketch UI changes (card 16). Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-14 in the checklist, and append the result to `.exchange`.
