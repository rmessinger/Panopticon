# Task Card `WI-W5-01`: Implement the front-strip partition

| Field | Value |
|---|---|
| Card ID | `WI-W5-01` |
| Roadmap phase | [Track W · W5](../../track-w-western-front.md): distributed regional simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B6-01`, `WI-B6-02`, W1 topology cards |
| Diagrams | [W5 §7](../../deep-dives/w5-distributed-regional-simulation.md#7-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-B6-01`, `WI-B6-02`, and W1 topology cards are Done.

Implement `WesternFrontFrontStripPartition` for synthetic and generated Western Front topologies.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §2.
- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §4.

## Steps

1. Build strips perpendicular to the front line.
2. Assign both sides of each frontage to the same shard.
3. Expose neighbours and halo regions.
4. Add tests for 1, 2, 4, and 8 shards on synthetic topology.

## Files likely to change

- Western Front partition files
- Tests

## Acceptance criteria

- [ ] No contested pair crosses shards.
- [ ] Neighbour sets include only lateral strips.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~FrontStripPartition"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No load balancing.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
