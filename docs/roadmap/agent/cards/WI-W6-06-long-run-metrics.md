# Task Card `WI-W6-06`: Collect W6 long-run metrics

| Field | Value |
|---|---|
| Card ID | `WI-W6-06` |
| Roadmap phase | [Track W · W6](../../track-w-western-front.md): long-run campaign experiment |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W6-03`, `WI-W6-05` |
| Diagrams | [W6 §6](../../deep-dives/w6-long-run-campaign.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W6-03` and `WI-W6-05` are Done.

Record W6 memory, artifact, wall-clock, save/load, and reconstruction metrics.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w6-long-run-campaign.md` §4.
- B1 harness design from the track B implementation plan.

## Steps

1. Add metric collection for tick cost, memory, artifact size, index size, save/load time, and reconstruction latency.
2. Emit JSON and CSV summary records.
3. Add a small fixture test for schema and units.

## Files likely to change

- Western Front metrics files
- Tests

## Acceptance criteria

- [ ] Metrics include units and campaign frame ranges.
- [ ] JSON and CSV summaries are produced for a fixture run.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontLongRunMetrics"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No performance target enforcement.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
