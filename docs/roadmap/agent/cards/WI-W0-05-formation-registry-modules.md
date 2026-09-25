# Task Card `WI-W0-05`: Add state modules and the formation registry

| Field | Value |
|---|---|
| Card ID | `WI-W0-05` |
| Roadmap phase | [Track W · W0](../../track-w-western-front.md): charter fixture |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-02` |
| Diagrams | [W0 §12](../../deep-dives/w0-charter-fixture.md#12-architecture-diagrams) |

## Goal

Hold Western Front state on the shared `GameObject` runtime through data-only behavior modules, and map stable keys to objects.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §2 and §4.
- `src/Panopticon.Simulation/Simulation/Transport/TransportGraphDomainModel.cs:51-92`: code-built
  definitions and `ModuleDataContainer`.
- `src/Panopticon.Simulation/Simulation/Transport/TransportVehicleUpdate.cs`: module data pattern.
- `src/Panopticon.Simulation/Logic/Object/BehaviorModule.cs`: behavior base type.

## Steps

1. Add state classes `FormationState`, `SquadState`, `DepotState`, `RegionState`, `LinkState`,
   `TheaterState`, `PoolState`, `LedgerState`, and `AggregateState`, one property per §4 field;
   conserved properties have `internal` setters.
2. Add data-only modules `FormationBehavior`, `SquadBehavior`, `DepotBehavior`, and
   `RegionControllerBehavior` with module data classes; none overrides a per-frame update.
3. Add `FormationRegistry`: `CreateFormation`, `CreateSquad`, `CreateDepot`, `CreateRegionController`,
   key-to-object-id lookups both ways, `SquadsOf(formationKey)`, and
   `FormAggregate(regionKey, side, members)`.
4. Tests on `MockedGameTest`: objects carry the expected module; keys round-trip to object ids;
   squads map to their formation; aggregate members list in ordinal order.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/State/*.cs`
- `src/Panopticon.Simulation.WesternFront/Runtime/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/FormationRegistryTests.cs`

## Acceptance criteria

- [ ] No module added here overrides a per-frame update method.
- [ ] Creating a duplicate key throws `InvalidOperationException`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~FormationRegistry"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No scenario values, ledger, or domain adapter. Conserved fields change only in tests until `WI-W0-08`.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-W0-06`.
