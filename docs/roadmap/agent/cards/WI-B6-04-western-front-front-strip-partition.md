# Task Card `WI-B6-04`: Implement B6 front-strip partition for W

| Field | Value |
|---|---|
| Card ID | `WI-B6-04` |
| Roadmap phase | [Track B · B6](../../track-b-scale.md): distributed simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B6-01`, `WI-B6-02`, W1 topology cards |
| Diagrams | [B6 §9](../../deep-dives/b6-distributed-simulation.md#9-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-B6-01`, `WI-B6-02`, and W1 topology cards are Done.

Implement the Western Front partition scheme that B6-2 uses.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §5.
- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §2.

## Steps

1. Add the Western Front `IPartitionScheme` implementation.
2. Partition by frontage strips perpendicular to the front.
3. Validate no contested pair crosses shards.
4. Expose neighbours and halo keys.

## Files likely to change

- Western Front partition files
- Tests

## Acceptance criteria

- [ ] Synthetic topology passes for 1, 2, 4, and 8 shards.
- [ ] No contested pair crosses shards.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~FrontStripPartition"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No generic load balancing.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
