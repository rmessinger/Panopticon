# Task Card `WI-B6-08`: Implement compact boundary codec

| Field | Value |
|---|---|
| Card ID | `WI-B6-08` |
| Roadmap phase | [Track B · B6](../../track-b-scale.md): distributed simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B6-03`, `WI-B6-06` |
| Diagrams | [B6 §9](../../deep-dives/b6-distributed-simulation.md#9-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-B6-03` and `WI-B6-06` are Done.

Add the B6-6 compact binary encoding for boundary messages.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §6.
- JSON fixtures from `WI-B6-06`.

## Steps

1. Define versioned binary envelope and payload tags.
2. Encode and decode every boundary message group.
3. Add compatibility tests comparing JSON and binary semantic messages.
4. Reject unknown version and malformed payloads.

## Files likely to change

- B6 transport codec files
- Tests

## Acceptance criteria

- [ ] Binary and JSON fixtures decode to equal semantic messages.
- [ ] Malformed payloads fail with diagnostics.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~CompactBoundaryCodec"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No socket or process management.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
