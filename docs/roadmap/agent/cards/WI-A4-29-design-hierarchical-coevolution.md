# Task Card `WI-A4-29`: Design hierarchical co-evolution

| Field | Value |
|---|---|
| Card ID | `WI-A4-29` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3f hierarchical training harness |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-A4-16`, `WI-A4-28` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Resolve joint versus alternating evolution, partner sampling, below-match fitness, seed pairing, and
the report schema for three interacting rank populations.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-decentralized-hierarchical-ai.md` §6.
- `docs/roadmap/implementation/track-a-a4-ai-policy-evolution.md`.
- Landed flat evolution runner and hierarchical inference policies.
- Existing outcome and T0 metric types.

## Steps

1. Choose and specify one co-evolution schedule.
2. Define partner sampling and credit assignment for each rank.
3. Define match-level and allowed shaped fitness components.
4. Define reproducibility inputs, evaluation limits, failure behavior, and report fields.
5. Update the hierarchy deep dive, diagrams, and open-question decisions.

## Files likely to change

- `docs/roadmap/deep-dives/a4-decentralized-hierarchical-ai.md`
- `docs/roadmap/open-questions.md`
- `docs/roadmap/implementation/track-a-a4-hierarchical-ai.md`

## Acceptance criteria

- [ ] Every population has an unambiguous evaluation and advancement rule.
- [ ] Inter-rank partner selection is bounded and reproducible.
- [ ] Fitness uses named existing data or explicitly adds a later prerequisite.
- [ ] Report ancestry and rank diversity are defined.

## Validation commands

```sh
grep -n "co-evolution\\|partner\\|fitness\\|seed" docs/roadmap/deep-dives/a4-decentralized-hierarchical-ai.md
```

## Out of scope / guardrails

No implementation or tuning.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-30`.
