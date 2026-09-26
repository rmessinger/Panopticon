# Task Card `WI-W4-02`: Bind W4 axes to Western Front parameters

| Field | Value |
|---|---|
| Card ID | `WI-W4-02` |
| Roadmap phase | [Track W · W4](../../track-w-western-front.md): command and logistics experiment |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W4-01`, W1 parameter cards, W2 activation cards |
| Diagrams | [W4 §6](../../deep-dives/w4-command-logistics-experiment.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W4-01`, W1 parameter cards, and W2 activation cards are Done.

Bind matrix axis values to `WesternFrontParameters`, combat variant, and activation policy selection.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w4-command-logistics-experiment.md` §2.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §8.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §3.

## Steps

1. Add typed binding targets for order latency, reconnaissance probability, link capacity, activation policy, and combat variant.
2. Add tests proving each axis changes only its intended target.
3. Record bound values in run output metadata.

## Files likely to change

- Western Front experiment binding files
- Tests

## Acceptance criteria

- [ ] Each axis has a focused binding test.
- [ ] Run output identifies all W4 axis values and seed.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~W4ParameterBinding"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No new axis values.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
