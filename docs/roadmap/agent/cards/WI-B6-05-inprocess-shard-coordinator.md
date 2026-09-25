# Task Card `WI-B6-05`: Implement in-process shard coordinator

| Field | Value |
|---|---|
| Card ID | `WI-B6-05` |
| Roadmap phase | [Track B · B6](../../track-b-scale.md): distributed simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B6-04`, W1 scheduler cards |
| Diagrams | [B6 §9](../../deep-dives/b6-distributed-simulation.md#9-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-B6-04` and W1 scheduler cards are Done.

Run multiple Western Front shard hosts in one process with lockstep barriers.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §5.
- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §3.

## Steps

1. Create shard coordinator and shard host setup.
2. Step each shard for one lockstep frame.
3. Exchange boundaries after each frame.
4. Check ownership and conservation invariants.

## Files likely to change

- B6 coordinator files
- Western Front shard files
- Tests

## Acceptance criteria

- [ ] Compressed and tactical modes use the correct barrier cadence.
- [ ] Invariant failures identify shard id and frame.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ShardCoordinator"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No multi-process transport.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
