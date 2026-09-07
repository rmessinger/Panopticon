# Task Card `WI-A4-24`: Design hierarchical neural policies

| Field | Value |
|---|---|
| Card ID | `WI-A4-24` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3e first per-rank neural networks |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-A4-23` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Freeze feature vectors, network shapes, genome layouts, action decoding, and bounds for soldier,
commander, and general inference.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-decentralized-hierarchical-ai.md` §§2 through 6.
- `docs/roadmap/deep-dives/neural-network-ai-policy.md`: contract style.
- Landed per-rank contexts and reference policies from `WI-A4-20` through `WI-A4-22`.

## Steps

1. Create `docs/roadmap/deep-dives/hierarchical-neural-network-policies.md`.
2. Define stable indexed input/output tables and normalization for all ranks.
3. Define exact network shapes, activations, flat genome layouts, tie-breaking, and invalid-input
   behavior.
4. Bound squad and squad-count encodings explicitly.
5. Add hand-computable examples and update hierarchy diagrams.

## Files likely to change

- `docs/roadmap/deep-dives/hierarchical-neural-network-policies.md` (new)
- `docs/roadmap/deep-dives/a4-decentralized-hierarchical-ai.md`
- `docs/roadmap/README.md`

## Acceptance criteria

- [ ] Every feature and output has a stable index and normalization.
- [ ] Every genome length is derivable from the tables.
- [ ] Overflow, padding, tie, and invalid-length behavior is explicit.
- [ ] No implementation choice remains for `WI-A4-25` through `WI-A4-27`.

## Validation commands

```sh
grep -n "TBD\\|pending" docs/roadmap/deep-dives/hierarchical-neural-network-policies.md
```

## Out of scope / guardrails

No training scheme or code.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-25` through `WI-A4-27`.
