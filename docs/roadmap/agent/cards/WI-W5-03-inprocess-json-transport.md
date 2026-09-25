# Task Card `WI-W5-03`: Add in-process JSON boundary transport

| Field | Value |
|---|---|
| Card ID | `WI-W5-03` |
| Roadmap phase | [Track W · W5](../../track-w-western-front.md): distributed regional simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B6-03`, `WI-W5-02` |
| Diagrams | [W5 §7](../../deep-dives/w5-distributed-regional-simulation.md#7-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-B6-03` and `WI-W5-02` are Done.

Implement the W5 JSON transport for frame-ordered in-process boundary messages.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §4.
- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §5.

## Steps

1. Add JSON serialization for `WesternFrontBoundaryMessage`.
2. Queue messages by frame, source, and target.
3. Reject duplicate and out-of-order messages.
4. Add round-trip and ordering tests.

## Files likely to change

- Western Front transport files
- Tests

## Acceptance criteria

- [ ] Messages round-trip exactly for deterministic JSON fixtures.
- [ ] Duplicate or out-of-order frame delivery is diagnosed.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontJsonTransport"
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
