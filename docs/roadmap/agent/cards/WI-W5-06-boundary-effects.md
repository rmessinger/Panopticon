# Task Card `WI-W5-06`: Apply logistics and command boundary effects

| Field | Value |
|---|---|
| Card ID | `WI-W5-06` |
| Roadmap phase | [Track W · W5](../../track-w-western-front.md): distributed regional simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W5-04`, W1 logistics and command cards |
| Diagrams | [W5 §7](../../deep-dives/w5-distributed-regional-simulation.md#7-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W5-04` and W1 logistics/command cards are Done.

Resolve lateral logistics shipments and command messages exactly once across shard boundaries.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §4.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §3 and §5.

## Steps

1. Represent cross-shard shipments and messages as boundary effects.
2. Apply effects only on the target shard and acknowledge them.
3. Record send, apply, and ack metrics.
4. Add duplicate suppression tests.

## Files likely to change

- Western Front boundary effect files
- Tests

## Acceptance criteria

- [ ] Each boundary effect has one send, one apply, and one ack.
- [ ] Duplicate delivery does not duplicate holdings or messages.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontBoundaryEffect"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No RTS combat boundary effects.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
