# Task Card `WI-W4-04`: Create the W4 cell result schema

| Field | Value |
|---|---|
| Card ID | `WI-W4-04` |
| Roadmap phase | [Track W · W4](../../track-w-western-front.md): command and logistics experiment |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W4-02`, `WI-W4-03` |
| Diagrams | [W4 §6](../../deep-dives/w4-command-logistics-experiment.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W4-02` and `WI-W4-03` are Done.

Add the W4 per-cell and per-seed result schema for A5 comparison.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w4-command-logistics-experiment.md` §3.
- `docs/roadmap/deep-dives/a5-visualization.md` §5.

## Steps

1. Define result records keyed by matrix cell and seed.
2. Capture outcome, casualties, logistics, command latency, activation, cost, and artifact references.
3. Add JSON round-trip tests.
4. Ensure failed information-audit cells retain diagnostic payloads.

## Files likely to change

- W4 result schema files
- Tests

## Acceptance criteria

- [ ] JSON result round-trips preserve axis values, seed, metrics, and artifacts.
- [ ] Audit failures are represented without losing partial metrics.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~W4CellResult"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No statistical report generation.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
