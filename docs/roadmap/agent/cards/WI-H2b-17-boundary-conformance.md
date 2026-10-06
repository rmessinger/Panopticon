# Task Card `WI-H2b-17`: Add cross-boundary conformance tests

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-17` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-H2b-16` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Prove every inventoried boundary normalizes once on read and inverts on write.

## Context to read (and only this)

- Card 01 inventory and card 02 fixtures.
- Existing tests added by cards 03-16.

## Steps

1. Add one parameterized suite that loads each legacy fixture, checks metric values, writes back, and compares bytes.
2. Add metric-source idempotence tests for glTF, NIF, and H2a metric tags.
3. Add a source scan test that fails on new `3.28084` or stray `0.3048` literals outside the helper.

## Files likely to change

- New conformance tests under `test/Panopticon.Simulation.Tests/`.

## Acceptance criteria

- [ ] Every inventory row is covered by a conformance case or an explicit exemption.
- [ ] Legacy write round trips are byte-identical.
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

No production code changes except bugs the suite exposes, each reported first. Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-17 in the checklist, and append the result to `.exchange`.
