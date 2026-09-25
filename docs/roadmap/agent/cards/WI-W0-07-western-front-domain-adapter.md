# Task Card `WI-W0-07`: Add the Western Front domain adapter

| Field | Value |
|---|---|
| Card ID | `WI-W0-07` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-04`, `WI-W0-06`, `WI-A4-44` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Add `WesternFrontDomainModel` implementing `ISimulationDomainModel` and `IDomainStateProjection`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §8.
- `src/Panopticon.Simulation/Simulation/ISimulationDomainModel.cs:15-63`.
- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §5.

## Steps

1. Implement every §8 mapping.
2. Subscribe scheduler advancement in `Initialize`.
3. Capture state records in ordinal key order.
4. Capture entities in object-id order and use soldier cell topology for tactical objects.
5. Add tests for domain id, topology keys, state capture, schema validation, and scheduler subscription.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/WesternFrontDomainModel.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontDomainModelTests.cs`

## Acceptance criteria

- [ ] `CaptureState` includes theater, operational, aggregate, and soldier records.
- [ ] `CaptureEntities` returns soldiers, not squads.
- [ ] `ResolveInteractionAuthority` maps cell keys to containing region controllers.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontDomainModel"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No ledger residual implementation beyond exposing captured values.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
