# Task Card `WI-W1-09`: Add shipments, interdiction, and consumption

| Field | Value |
|---|---|
| Card ID | `WI-W1-09` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-08`, `WI-W1-03` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-08` and `WI-W1-03` are Done.

Advance shipments, apply interdiction and repair, and consume supplies.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §6-§7.
- W1 random streams from `WI-W1-03`.
- W1 logistics allocation from `WI-W1-08`.

## Steps

1. Advance shipments by travel time and deliver them through the ledger.
2. Apply interdiction damage from artillery effects on link endpoint regions.
3. Apply engineer repair per operational tick.
4. Add `ConsumptionSystem` for SAA, artillery rounds, and replacement absorption.
5. Add tests for shipment arrival, link damage, repair, stock-days, and consumed-supply sinks.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Systems/LogisticsSystem.cs`
- `src/Panopticon.Simulation.WesternFront/Systems/ConsumptionSystem.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/ShipmentConsumptionTests.cs`

## Acceptance criteria

- [ ] Delivered shipments transfer conserved amounts from shipment holdings to formations or depots.
- [ ] Consumption sinks to ledger fields only.
- [ ] Interdiction changes later effective link capacity.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ShipmentConsumption"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No combat casualty model.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
