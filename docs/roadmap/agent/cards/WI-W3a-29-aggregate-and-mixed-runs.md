# Task Card `WI-W3a-29`: Run aggregate and mixed arms

| Field | Value |
|---|---|
| Card ID | `WI-W3a-29` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W3a-28`, `WI-W2-14` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-28` and `WI-W2-14` are Done.

Add Sedan mixed and aggregate experiment arms with 64 seeds each and error distributions against the reference.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §10-§11 and §12.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §7-§8.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §8 and §11.

## Steps

1. Define the mixed arm using `ContactActivationPolicy`.
2. Define the aggregate arm using W1 aggregate combat only.
3. Run 64 seeds per arm over the Sedan 60 h window.
4. Compare casualty, ground, ammunition, artillery, dwell, and terrain distributions against the reference.
5. Store error distributions in the run output without permanent thresholds.

## Files likely to change

- Western Front Sedan experiment files
- Western Front error-report tests
- Run-output artifact configuration

## Acceptance criteria

- [ ] Mixed and aggregate arms each run 64 seeds.
- [ ] Error distributions compare against the all-tactical reference.
- [ ] Report declares distributions without setting unsupported pass/fail thresholds.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SedanAggregateMixedRuns"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not reduce seed count for speed without marking the card Blocked.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
