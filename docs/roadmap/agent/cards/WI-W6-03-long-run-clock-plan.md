# Task Card `WI-W6-03`: Implement the long-run clock plan

| Field | Value |
|---|---|
| Card ID | `WI-W6-03` |
| Roadmap phase | [Track W · W6](../../track-w-western-front.md): long-run campaign experiment |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W6-01`, W0 scheduler cards |
| Diagrams | [W6 §6](../../deep-dives/w6-long-run-campaign.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W6-01` and W0 scheduler cards are Done.

Validate one-minute operational ticks and one-hour campaign ticks over the July-November duration.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w6-long-run-campaign.md` §1 and §4.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §7.

## Steps

1. Add a long-run scheduler test that advances 201600 operational ticks without per-frame host cost if possible.
2. Assert campaign tick counts and theater time.
3. Verify mode transitions remain on operational boundaries.

## Files likely to change

- Western Front scheduler tests

## Acceptance criteria

- [ ] 201600 one-minute ticks cover the declared duration without drift.
- [ ] Campaign cadence is one hour.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontLongRunClock"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No full campaign simulation.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
