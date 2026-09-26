# Task Card `WI-B6-07`: Run B6-2 Western Front shard evidence

| Field | Value |
|---|---|
| Card ID | `WI-B6-07` |
| Roadmap phase | [Track B · B6](../../track-b-scale.md): distributed simulation |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-B6-05`, `WI-B6-06`, W1 evidence cards |
| Diagrams | [B6 §9](../../deep-dives/b6-distributed-simulation.md#9-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-B6-05`, `WI-B6-06`, and W1 evidence cards are Done.

Validate B6-2 with Western Front in-process shards.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §7.
- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §5.

## Steps

1. Run shard counts 1, 2, 4, and 8 on the W1 synthetic scenario.
2. Assert ownership, conservation, boundary-effect, and handoff invariants.
3. Record comparison metrics against the one-shard reference.
4. Emit A5-compatible artifacts.

## Files likely to change

- B6 evidence tests
- A5 artifacts

## Acceptance criteria

- [ ] All shard counts pass invariants.
- [ ] Comparison metrics are recorded without requiring byte identity.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~B6WesternFrontShardEvidence"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No RTS/Bepu sharding.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
