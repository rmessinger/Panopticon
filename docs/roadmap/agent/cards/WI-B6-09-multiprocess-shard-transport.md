# Task Card `WI-B6-09`: Implement multi-process shard transport

| Field | Value |
|---|---|
| Card ID | `WI-B6-09` |
| Roadmap phase | [Track B · B6](../../track-b-scale.md): distributed simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B6-08`, `WI-B6-07` |
| Diagrams | [B6 §9](../../deep-dives/b6-distributed-simulation.md#9-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-B6-08` and `WI-B6-07` are Done.

Replace the in-process transport with a frame-ordered multi-process transport.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §6.
- `src/Panopticon.Simulation/Network/NetworkMessageBuffer.cs:10-78`: frame-ordered pattern.

## Steps

1. Create a process-local transport abstraction using the compact codec.
2. Preserve frame ordering and neighbour-pair ordering.
3. Add handshake with shard id, schema, and scenario hash.
4. Run a two-process fixture test if the test environment supports it; otherwise add a loopback process test harness.

## Files likely to change

- B6 multi-process transport files
- Tests

## Acceptance criteria

- [ ] Two shard processes exchange frame-ordered messages.
- [ ] Schema or scenario hash mismatch aborts before stepping.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~MultiProcessShardTransport"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No availability or failover model.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
