# Task Card `WI-W6-07`: Report active fidelity approximation error

| Field | Value |
|---|---|
| Card ID | `WI-W6-07` |
| Roadmap phase | [Track W · W6](../../track-w-western-front.md): long-run campaign experiment |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-W6-06`, W2 envelope report, W5 evidence |
| Diagrams | [W6 §6](../../deep-dives/w6-long-run-campaign.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W6-06`, W2 envelope report, and W5 evidence are Done.

Produce the approximation-error section for W6 based on active fidelity policies.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w6-long-run-campaign.md` §4.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §7.

## Steps

1. List every active fidelity policy and activation arm.
2. Link each policy to the W2 envelope or all-tactical reference evidence.
3. Flag any policy without an error declaration.
4. Write report artifact.

## Files likely to change

- W6 approximation report

## Acceptance criteria

- [ ] Every active fidelity policy has a declared error source.
- [ ] Missing declarations fail the report.

## Validation commands

```sh
python docs/roadmap/scripts/check_links.py docs/roadmap/deep-dives/w6-long-run-campaign.md || true
```

## Out of scope / guardrails

No new simulation model.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
