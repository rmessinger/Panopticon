# Task Card `WI-W0-07`: Implement the Western Front domain adapter

| Field | Value |
|---|---|
| Card ID | `WI-W0-07` |
| Roadmap phase | [Track W · W0](../../track-w-western-front.md): charter fixture |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-04`, `WI-W0-06`, `WI-A4-44` |
| Diagrams | [W0 §12](../../deep-dives/w0-charter-fixture.md#12-architecture-diagrams) |

## Goal

Implement `WesternFrontDomainModel : ISimulationDomainModel, IDomainStateProjection` per the §8 mapping.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §8.
- `src/Panopticon.Simulation/Simulation/ISimulationDomainModel.cs`.
- `src/Panopticon.Simulation/Simulation/Transport/TransportGraphDomainModel.cs`: adapter pattern.
- `src/Panopticon.Simulation/Simulation/State/IDomainStateProjection.cs`.

## Steps

1. Add `WesternFrontDomainModel.Create(IGameEngine, WesternFrontScenarioOptions, WesternFrontClockOptions)`
   wrapping scenario, topology, registry, and scheduler.
2. `Initialize` attaches the scheduler to `TickStarting` with the engine's `MsPerLogicFrame`.
3. Implement every §8 member. `CaptureState` emits records in ordinal key order through the
   `WI-W0-02` field indices; `aggregate.members` is the space-joined ordinal member list.
4. Tests on `MockedGameTest`: a host runs 10 frames; entity captures are id-ordered; topology keys
   per entity type; authority for a `link:` key returns the tail region controller id; state record
   counts per kind match the scenario; metric series names match §8.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/WesternFrontDomainModel.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontDomainModelTests.cs`

## Acceptance criteria

- [ ] `SimulationHost` runs the domain with no change to host code.
- [ ] Every captured record passes schema vector-length checks.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontDomainModel"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

`conservation-residual` metrics may report zero placeholders until `WI-W0-08`.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-W0-08`.
