# Task Card `WI-B6-06`: Implement JSON boundary transport

| Field | Value |
|---|---|
| Card ID | `WI-B6-06` |
| Roadmap phase | [Track B · B6](../../track-b-scale.md): distributed simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B6-03`, `WI-B6-05` |
| Diagrams | [B6 §9](../../deep-dives/b6-distributed-simulation.md#9-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-B6-03` and `WI-B6-05` are Done.

Serialize and deliver B6-2 in-process boundary messages as JSON.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §5.
- Boundary message design from `WI-B6-03`.

## Steps

1. Add JSON encoder and decoder for boundary messages.
2. Preserve deterministic field ordering where the serializer permits it.
3. Add corruption, duplicate, and out-of-order diagnostics.
4. Integrate with the in-process transport queue.

## Files likely to change

- B6 transport files
- Tests

## Acceptance criteria

- [ ] JSON fixtures round-trip to the same semantic message.
- [ ] Duplicate or out-of-order messages fail before apply.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~JsonBoundaryTransport"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No compact binary encoding.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
