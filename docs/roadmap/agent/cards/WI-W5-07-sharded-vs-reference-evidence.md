# Task Card `WI-W5-07`: Compare sharded runs with the reference

| Field | Value |
|---|---|
| Card ID | `WI-W5-07` |
| Roadmap phase | [Track W · W5](../../track-w-western-front.md): distributed regional simulation |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W5-05`, `WI-W5-06`, `WI-A5-05` |
| Diagrams | [W5 §7](../../deep-dives/w5-distributed-regional-simulation.md#7-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W5-05`, `WI-W5-06`, and `WI-A5-05` are Done.

Run the W5 evidence matrix and compare sharded results with the single-process reference.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §5.
- `docs/roadmap/deep-dives/a5-visualization.md` §5.

## Steps

1. Run shard counts 1, 2, 4, and 8 on the selected W scenario.
2. Assert ownership, conservation, and boundary-message invariants.
3. Compare outcomes within the W2 envelope rather than byte identity.
4. Persist A5 divergence-localization artifacts on failures.

## Files likely to change

- Western Front W5 evidence tests
- A5 comparison artifacts

## Acceptance criteria

- [ ] All shard counts pass ownership and conservation invariants.
- [ ] Outcome comparison reports stay within the declared envelope or preserve artifacts for review.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontShardedEvidence"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No automatic tuning to force parity.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
