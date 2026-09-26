# Task Card `WI-W1-12`: Add morale, cohesion, outcome, and terrain effects

| Field | Value |
|---|---|
| Card ID | `WI-W1-12` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-10`, `WI-W1-11` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-10` and `WI-W1-11` are Done.

Apply cohesion, morale, withdrawal, front-line, crater, wire, and trench effects.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §8.2-§8.3.
- W1 combat variants from `WI-W1-10` and `WI-W1-11`.
- W0 region terrain state.

## Steps

1. Update cohesion from casualty fraction and fatigue per operational tick.
2. Update morale at campaign cadence.
3. Apply withdrawal when cohesion or morale crosses breakpoints.
4. Record front-line metric samples per sector.
5. Apply crater and wire changes from artillery fire and terrain protection modifiers in combat.
6. Add tests for withdrawal, region capture, protection, and front-line metrics.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Combat/*.cs`
- `src/Panopticon.Simulation.WesternFront/Systems/AggregateCombatSystem.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/MoraleTerrainOutcomeTests.cs`

## Acceptance criteria

- [ ] A platoon below breakpoint withdraws one region toward its rear when possible.
- [ ] Region capture occurs only after defender withdrawal or destruction.
- [ ] Terrain modifiers change expected casualties in the tested direction.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~MoraleTerrainOutcome"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No tactical promotion.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
