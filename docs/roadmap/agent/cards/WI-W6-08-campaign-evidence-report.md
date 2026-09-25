# Task Card `WI-W6-08`: Write the W6 campaign evidence report

| Field | Value |
|---|---|
| Card ID | `WI-W6-08` |
| Roadmap phase | [Track W · W6](../../track-w-western-front.md): long-run campaign experiment |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-W6-04`, `WI-W6-06`, `WI-W6-07` |
| Diagrams | [W6 §6](../../deep-dives/w6-long-run-campaign.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W6-04`, `WI-W6-06`, and `WI-W6-07` are Done.

Produce the final W6 evidence report.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w6-long-run-campaign.md` §4-§5.
- Artifacts from prior W6 cards.

## Steps

1. Summarize duration, tick counts, scenario bindings, provenance, save/load, replay, metrics, and approximation error.
2. Record artifact paths and hashes.
3. State whether historical OOB was synthetic or accepted by decision.
4. List follow-up gaps.

## Files likely to change

- W6 evidence report

## Acceptance criteria

- [ ] Report covers all W6 evidence categories.
- [ ] Report includes provenance and does not include operator source datasets.

## Validation commands

```sh
python docs/roadmap/scripts/check_links.py docs/roadmap/deep-dives/w6-long-run-campaign.md || true
```

## Out of scope / guardrails

No code changes.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
