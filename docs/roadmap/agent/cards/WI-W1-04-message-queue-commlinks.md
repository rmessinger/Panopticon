# Task Card `WI-W1-04`: Add message queue, comm links, and communication system

| Field | Value |
|---|---|
| Card ID | `WI-W1-04` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-02`, `WI-W1-03` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-02` and `WI-W1-03` are Done.

Implement delayed orders and reports over communication links.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §2 and §4.1-§4.2.
- W0 scheduler and ledger types.
- W1 schema v2 types from `WI-W1-02`.

## Steps

1. Add `FormationOrder`, `FormationReport`, `WesternFrontMessage`, `MessageQueue`, `CommLink`, and `CommType`.
2. Implement `CommunicationSystem` as an operational `IWesternFrontSystem`.
3. Select fastest available link with fallback and disruption.
4. Record delivery, acknowledgement, expiry, and loss in schema v2 records and metrics.
5. Add tests for latency ordering, fallback, expiry, and repair.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Command/*.cs`
- `src/Panopticon.Simulation.WesternFront/Systems/CommunicationSystem.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/CommunicationSystemTests.cs`

## Acceptance criteria

- [ ] Due messages deliver on the first operational tick at or after due time.
- [ ] Cut telephone links fall back to runner or wireless.
- [ ] Metrics include `order-latency` and `order-loss` per echelon pair.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~CommunicationSystem"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No policy decisions beyond delivery.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
