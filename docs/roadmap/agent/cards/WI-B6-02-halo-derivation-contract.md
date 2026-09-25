# Task Card `WI-B6-02`: Design halo derivation rules

| Field | Value |
|---|---|
| Card ID | `WI-B6-02` |
| Roadmap phase | [Track B · B6](../../track-b-scale.md): distributed simulation |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [B6 §9](../../deep-dives/b6-distributed-simulation.md#9-architecture-diagrams) |

## Goal

Define how domains derive halo state from topology and interaction locality.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §4.
- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §2.

## Steps

1. Document halo inputs for Western Front, RTS, graph, and zone domains.
2. Define maximum age and ownership restrictions.
3. Define diagnostics for missing, stale, or mutable halo state.
4. Add test cases to the implementation plan.

## Files likely to change

- B6 implementation plan or design artifact

## Acceptance criteria

- [ ] Halo rules distinguish owned and read-only state.
- [ ] Western Front halo includes lateral regions, boundary links, and pending messages.

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
