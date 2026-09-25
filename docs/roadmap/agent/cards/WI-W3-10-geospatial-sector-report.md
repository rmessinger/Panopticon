# Task Card `WI-W3-10`: Write the W3 evidence report

| Field | Value |
|---|---|
| Card ID | `WI-W3-10` |
| Roadmap phase | [Track W · W3](../../track-w-western-front.md): geospatial sector |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-W3-07`, `WI-W3-08`, `WI-W3-09` |
| Diagrams | [W3 §6](../../deep-dives/w3-geospatial-sector.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3-07`, `WI-W3-08`, and `WI-W3-09` are Done.

Produce the W3 evidence report summarizing payload validation, topology derivation, and W1/W2 evidence.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3-geospatial-sector.md` §4-§5.
- Evidence artifacts from prior W3 cards.

## Steps

1. Collect topology counts, control-point status, and source-reference coverage.
2. Summarize W1 and W2 evidence outcomes.
3. Record operator-local Somme proof status if available.
4. Link the report from the implementation plan or phase evidence location.

## Files likely to change

- W3 evidence report location chosen by prior cards

## Acceptance criteria

- [ ] Report includes control-point status, topology counts, W1/W2 evidence, and provenance summary.
- [ ] No raw operator source data is included.

## Validation commands

```sh
python docs/roadmap/scripts/check_links.py docs/roadmap/deep-dives/w3-geospatial-sector.md || true
```

## Out of scope / guardrails

No new implementation code.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
