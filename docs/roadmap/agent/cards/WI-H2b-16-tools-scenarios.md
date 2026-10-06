# Task Card `WI-H2b-16`: Convert tools, scenarios, and unit documentation

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-16` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-04`, `WI-H2b-12`, `WI-H2b-13`, `WI-H2b-14`, `WI-H2b-15` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Bring authored scenarios, ReplaySketch, TestHarness captures, inspectors, and docs onto meters.

## Context to read (and only this)

- `src/Panopticon.Tools.ReplaySketch.Core/` and `.Avalonia/` length displays and scenario layouts.
- `src/Panopticon.Tools.TestHarness/EngagementCaptureRunner.cs` and `scripts/annotate_engagement_gif.py`.
- Authored scenario data and inspectors listed by card 01.
- `docs/developer-guide.md` for the units section.

## Steps

1. Convert authored scenario lengths with explicit migration; label tool output in meters.
2. Add the units section to `docs/developer-guide.md`; link to it from `docs/maps.md` instead of restating.
3. Verify ReplaySketch map and model panels in screenshot mode.

## Files likely to change

- Files named by card 02.
- `docs/developer-guide.md`, `docs/maps.md`.

## Acceptance criteria

- [ ] ReplaySketch map and model screenshots match pre-cutover framing.
- [ ] Capture manifests state their unit.
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

No retroactive rewrite of historical investigation docs. Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-16 in the checklist, and append the result to `.exchange`.
