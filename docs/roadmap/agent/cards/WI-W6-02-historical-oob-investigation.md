# Task Card `WI-W6-02`: Investigate historical OOB source format

| Field | Value |
|---|---|
| Card ID | `WI-W6-02` |
| Roadmap phase | [Track W · W6](../../track-w-western-front.md): long-run campaign experiment |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-W6-01` |
| Diagrams | [W6 §6](../../deep-dives/w6-long-run-campaign.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W6-01` is Done.

Investigate and decide whether W6 accepts an operator-supplied historical OOB format or remains synthetic.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w6-long-run-campaign.md` §1-§2.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §3.

## Steps

1. Evaluate candidate OOB fields against W formation keys down to platoon.
2. Define provenance requirements and validation rules.
3. Record the decision in the W6 implementation plan or decision log owner if accepted by the coordinator.
4. Create follow-up parser cards only if the decision accepts a format.

## Files likely to change

- Investigation note or decision artifact

## Acceptance criteria

- [ ] Decision states accepted format or explicitly keeps synthetic OOB.
- [ ] No source dataset is committed.

## Validation commands

```sh
python docs/roadmap/scripts/check_links.py docs/roadmap/deep-dives/w6-long-run-campaign.md || true
```

## Out of scope / guardrails

No parser or dataset import.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
