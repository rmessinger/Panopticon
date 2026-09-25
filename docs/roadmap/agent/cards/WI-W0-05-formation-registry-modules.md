# Task Card `WI-W0-05`: Add state modules and the formation registry

| Field | Value |
|---|---|
| Card ID | `WI-W0-05` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-02` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Add data-only behavior modules for W0 state and a registry mapping stable keys to objects.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §2 and §4.
- `src/Panopticon.Simulation/Simulation/Transport/TransportVehicleUpdate.cs:15-27`: data-only module pattern.
- `src/Panopticon.Simulation.WesternFront/State/` from `WI-W0-02`.

## Steps

1. Add state records for formation, soldier, depot, region, aggregate, pool, ledger, and link state.
2. Add `FormationBehavior`, `SoldierBehavior`, `DepotBehavior`, and `RegionControllerBehavior` with no per-frame update.
3. Add `FormationRegistry` mapping formation, soldier, depot, and region keys to object ids.
4. Add tests for registration, lookup, stable ordering, and soldier-to-formation membership.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Runtime/*.cs`
- `src/Panopticon.Simulation.WesternFront/State/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/FormationRegistryTests.cs`

## Acceptance criteria

- [ ] Registry maps one soldier key to its platoon formation.
- [ ] No W0 behavior module advances state per logic frame.
- [ ] Registry enumeration is ordinal by stable key.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~FormationRegistry"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No scenario construction or ledger mutation rules.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
