# Task Card `WI-H2b-03`: Add explicit legacy-to-metric conversion helpers

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-03` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-H2b-02` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Provide one source of truth for the legacy distance scale and its dimensional variants, with no runtime behavior change yet.

## Context to read (and only this)

- `docs/roadmap/deep-dives/h2b-metric-execution.md`: helper names, location, and dimensional table.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifTransformConverter.cs`: existing `MetersPerFoot` constant to retire later.
- `src/Panopticon.Mathematics/`: target project for the helper.

## Steps

1. Add the helper type named by card 02 with length, area, volume, inverse-length, velocity, and acceleration conversions in both directions.
2. Add tests: `10` legacy units is `3.048` m, `100` is `30.48` m, `30` units/s is `9.144` m/s, round trips at magnitudes up to `100000`, and squared thresholds scale by `s²`.
3. Do not call the helper from runtime code in this card.

## Files likely to change

- The helper file in `src/Panopticon.Mathematics/` named by card 02.
- Matching test file under `test/Panopticon.Simulation.Tests/Mathematics/`.

## Acceptance criteria

- [ ] Only the helper defines `0.3048`; no other new literal is added.
- [ ] Every dimensional exponent in the card 02 table has a tested conversion and inverse.
- [ ] No runtime call site changes.
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

No call-site migration, no generic parser changes, no mass or force conversion. Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-3 in the checklist, and append the result to `.exchange`.
