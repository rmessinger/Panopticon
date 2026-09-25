# Task Card `WI-W0-06`: Build the synthetic W0 scenario

| Field | Value |
|---|---|
| Card ID | `WI-W0-06` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-03`, `WI-W0-05` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Add `WesternFrontScenario.CreateSynthetic` producing the exact W0 fixture.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §5-§6.
- `src/Panopticon.Simulation/Simulation/Transport/TransportGraphDomainModel.cs:51-92`: `CreateDefault` pattern.
- `src/Panopticon.Simulation.WesternFront/` types from `WI-W0-03` and `WI-W0-05`.

## Steps

1. Add `WesternFrontScenarioOptions` with §6 strengths and stock defaults.
2. Build region controllers, depots, both sides' formation trees to platoon depth, both reserve aggregates, and the partial tactical promotion of `formation:blue/a1/c1/d1/b2/bn1/co1/pl1` into 50 soldiers in `cell:0.1/7,3`.
3. Player indices: blue 0, red 1.
4. Tests: formation counts per side and echelon, aggregate members, soldier count and totals, and the promoted platoon remainder.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Scenario/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontScenarioTests.cs`

## Acceptance criteria

- [ ] Formation plus soldier personnel of the promoted platoon equals 50.
- [ ] The promoted battalion totals 800 personnel, 16 MG, and 400000 SAA across descendants.
- [ ] Every created object is registered with a stable key.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontScenario"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No historical values. No gameplay systems.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
