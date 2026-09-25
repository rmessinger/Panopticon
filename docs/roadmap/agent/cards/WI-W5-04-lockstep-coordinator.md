# Task Card `WI-W5-04`: Build the W5 lockstep coordinator

| Field | Value |
|---|---|
| Card ID | `WI-W5-04` |
| Roadmap phase | [Track W · W5](../../track-w-western-front.md): distributed regional simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W5-03`, W0 scheduler cards |
| Diagrams | [W5 §7](../../deep-dives/w5-distributed-regional-simulation.md#7-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W5-03` and W0 scheduler cards are Done.

Coordinate multiple in-process Western Front shard hosts with the required barriers.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §3.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §7.

## Steps

1. Create coordinator setup for N shard hosts.
2. Run one barrier per operational tick in compressed mode.
3. Run one barrier per logic frame in tactical mode.
4. Apply received boundary messages before the next frame.
5. Add compressed and tactical barrier tests.

## Files likely to change

- Western Front shard coordinator files
- Tests

## Acceptance criteria

- [ ] Compressed mode exchanges once per operational tick.
- [ ] Tactical mode exchanges once per logic frame.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontShardCoordinator"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No networking.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
