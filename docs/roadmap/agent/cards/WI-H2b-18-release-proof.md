# Task Card `WI-H2b-18`: Prove metric real-content parity and release

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-18` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-H2b-17` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Show the metric engine reproduces final-H2a Generals and EE2 behavior scaled by `s`, then open the staging-to-main PR.

## Context to read (and only this)

- Pre-cutover evidence captured per card 02.
- `docs/roadmap/deep-dives/h2b-metric-core.md#validation-contract`.

## Steps

1. Re-run the Generals and EE2 P9 proofs and compare paths, shots, durations, and outcomes to the baseline.
2. Capture map and model screenshots and compare framing.
3. Report any determinism or golden-baseline deviation and wait for the user before re-baselining.
4. Open the staging-to-main PR; do not merge it.

## Files likely to change

- `docs/roadmap/deep-dives/h2b-metric-execution.md` results section.
- Approved, versioned metric baselines.
- `.exchange`.

## Acceptance criteria

- [ ] Every comparison is within card 02 tolerances or has a user-approved explanation.
- [ ] The staging-to-main PR is open with CI results.

## Validation commands

Standard validation plus the comparison commands pinned by `WI-H2b-02`.

## Out of scope / guardrails

No baseline update without user approval; no new gameplay changes. Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-18 in the checklist, and append the result to `.exchange`.
