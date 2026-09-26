# Task Card `WI-W1-08`: Add logistics allocation with largest remainder

| Field | Value |
|---|---|
| Card ID | `WI-W1-08` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-02`, `WI-W0-08` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-02` and `WI-W0-08` are Done.

Allocate integer supply shipments over capacitated links.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §6.
- W0 ledger from `WI-W0-08`.
- W1 shipment schema from `WI-W1-02`.

## Steps

1. Add demand records for artillery ammo, SAA, and replacements.
2. Add priority tiers from orders: attack, hold, reserve.
3. Implement proportional allocation with largest-remainder rounding within each tier.
4. Move dispatched stock through the ledger into `shipment` holdings.
5. Add tests for exact integer allocation and conservation.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Logistics/*.cs`
- `src/Panopticon.Simulation.WesternFront/Systems/LogisticsSystem.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/LogisticsAllocationTests.cs`

## Acceptance criteria

- [ ] Competing demands split capacity exactly with no lost remainder.
- [ ] Attack demand outranks hold and reserve demand.
- [ ] Shipment dispatch leaves conservation residuals empty.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~LogisticsAllocation"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No interdiction, arrival, or consumption.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
