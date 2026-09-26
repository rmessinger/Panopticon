# Task Card `WI-W4-05`: Generate W4 sensitivity report

| Field | Value |
|---|---|
| Card ID | `WI-W4-05` |
| Roadmap phase | [Track W · W4](../../track-w-western-front.md): command and logistics experiment |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-W4-04`, A2 report utilities or B1 statistics utilities |
| Diagrams | [W4 §6](../../deep-dives/w4-command-logistics-experiment.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W4-04` and reporting utilities are Done.

Produce the W4 distribution and sensitivity report from a completed run set.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w4-command-logistics-experiment.md` §3.
- B1/A2 report utilities from the implementation plan.

## Steps

1. Load W4 cell results.
2. Compute median and bootstrap confidence intervals for declared outputs.
3. Rank axis sensitivities.
4. Write a compact report artifact and tests against a tiny fixture.

## Files likely to change

- W4 report generator
- Tests
- Report artifact schema

## Acceptance criteria

- [ ] Report includes confidence intervals for every declared metric.
- [ ] Sensitivity ranking names each axis and metric.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~W4Sensitivity"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No interpretation as historical calibration.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
