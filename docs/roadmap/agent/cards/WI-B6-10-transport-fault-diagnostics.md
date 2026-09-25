# Task Card `WI-B6-10`: Add transport fault diagnostics

| Field | Value |
|---|---|
| Card ID | `WI-B6-10` |
| Roadmap phase | [Track B · B6](../../track-b-scale.md): distributed simulation |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-B6-09` |
| Diagrams | [B6 §9](../../deep-dives/b6-distributed-simulation.md#9-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-B6-09` is Done.

Diagnose late, missing, duplicate, corrupt, and out-of-order multi-process boundary frames.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §6.
- Transport from `WI-B6-09`.

## Steps

1. Add fault injection hooks to the test transport.
2. Inject late, missing, duplicate, corrupt, and out-of-order frames.
3. Assert the coordinator blocks or aborts with clear diagnostics.
4. Record diagnostics in run output.

## Files likely to change

- B6 transport diagnostics files
- Tests

## Acceptance criteria

- [ ] Each fault type produces a distinct diagnostic.
- [ ] The coordinator never applies a corrupt or out-of-order frame.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ShardTransportFault"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No degraded availability mode.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
