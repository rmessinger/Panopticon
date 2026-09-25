# Task Card `WI-W5-02`: Create Western Front shard slice state

| Field | Value |
|---|---|
| Card ID | `WI-W5-02` |
| Roadmap phase | [Track W · W5](../../track-w-western-front.md): distributed regional simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W5-01`, W1 state cards |
| Diagrams | [W5 §7](../../deep-dives/w5-distributed-regional-simulation.md#7-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W5-01` and W1 state cards are Done.

Represent owned and halo Western Front state for one shard.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §3.
- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §3.

## Steps

1. Add shard slice records keyed by W stable keys.
2. Separate owned records from halo records.
3. Attach shard id and ownership metadata to captured state.
4. Test duplicate owner rejection.

## Files likely to change

- Western Front shard state files
- Tests

## Acceptance criteria

- [ ] Each key is owned by exactly one shard.
- [ ] Halo records are read-only and cannot mutate ledger holdings.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontShardState"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No transport code.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
