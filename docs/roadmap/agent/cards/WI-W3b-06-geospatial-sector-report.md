# Task Card `WI-W3b-06`: Write the W3b evidence report

| Field | Value |
|---|---|
| Card ID | `WI-W3b-06` |
| Roadmap phase | [Track W · W3b](../../track-w-western-front.md): Somme sector |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-W3b-04`, `WI-W3b-05`, `WI-W3a-27` |
| Diagrams | [W3b §5](../../deep-dives/w3b-somme-sector.md#5-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3b-04`, `WI-W3b-05`, and `WI-W3a-27` are Done.

Produce the W3b evidence report summarizing payload validation, topology derivation, and W1/W2 evidence.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3b-somme-sector.md` §3-§4.
- Evidence artifacts from prior W3b cards.

## Steps

1. Collect topology counts, control-point status, and source-reference coverage.
2. Summarize W1 and W2 evidence outcomes.
3. Record operator-local Somme proof status if available.
4. Link the report from the implementation plan or phase evidence location.

## Files likely to change

- W3b evidence report location chosen by prior cards

## Acceptance criteria

- [ ] Report includes control-point status, topology counts, W1/W2 evidence, and provenance summary.
- [ ] No raw operator source data is included.

## Validation commands

```sh
python docs/roadmap/scripts/check_links.py docs/roadmap/deep-dives/w3b-somme-sector.md || true
```

## Out of scope / guardrails

No new implementation code.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
