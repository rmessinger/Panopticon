# Task Card `WI-B6-01`: Design the partition scheme contract

| Field | Value |
|---|---|
| Card ID | `WI-B6-01` |
| Roadmap phase | [Track B · B6](../../track-b-scale.md): distributed simulation |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [B6 §9](../../deep-dives/b6-distributed-simulation.md#9-architecture-diagrams) |

## Goal

Define the domain-parameterized `IPartitionScheme` contract and validate it on paper against Western Front, graph, zone, and future RTS domains.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §4.
- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §3.

## Steps

1. Write the contract shape and invariants in the B6 implementation plan or design artifact.
2. Specify stable-key assignment, neighbours, halo input, authority validation, and diagnostics.
3. Work through one Western Front strip example and one graph-cut example.
4. List source-edit follow-up cards.

## Files likely to change

- B6 implementation plan or design artifact

## Acceptance criteria

- [ ] Contract names assignment, neighbours, halo, authority, and diagnostics.
- [ ] Western Front example proves contested pairs stay within a shard.

## Validation commands

```sh
python docs/roadmap/scripts/check_links.py docs/roadmap/deep-dives/b6-distributed-simulation.md || true
```

## Out of scope / guardrails

No source code implementation.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
