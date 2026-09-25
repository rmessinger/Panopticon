# Task Card `WI-W5-05`: Implement cross-boundary handoff

| Field | Value |
|---|---|
| Card ID | `WI-W5-05` |
| Roadmap phase | [Track W · W5](../../track-w-western-front.md): distributed regional simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W5-04`, W2 tactical cards |
| Diagrams | [W5 §7](../../deep-dives/w5-distributed-regional-simulation.md#7-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W5-04` and W2 tactical cards are Done.

Move formations and tactical soldiers between lateral shards without duplication or loss.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §4.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §4-§6.

## Steps

1. Add handoff event creation when a key crosses a lateral boundary.
2. Keep source ownership through frame N and target adoption at N+1.
3. Move conserved holdings via ledger events.
4. Drop source ownership only after acknowledgement.
5. Add zero-owner and two-owner regression tests.

## Files likely to change

- Western Front handoff files
- Tests

## Acceptance criteria

- [ ] Ownership is exactly one across every tested handoff.
- [ ] Conserved holdings match before and after handoff.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontHandoff"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No contested combat crossing shards.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
