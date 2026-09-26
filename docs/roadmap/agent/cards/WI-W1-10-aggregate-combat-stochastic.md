# Task Card `WI-W1-10`: Add stochastic aggregate combat

| Field | Value |
|---|---|
| Card ID | `WI-W1-10` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-03`, `WI-W1-09` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-03` and `WI-W1-09` are Done.

Resolve platoon-level stochastic aggregate combat per operational tick.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §8.1-§8.2.
- W1 random streams and parameters.
- W0 ledger and topology.

## Steps

1. Add engagement discovery for adjacent or attacking platoon region pairs.
2. Compute aimed-fire and area-fire lambda values from parameters.
3. Draw Poisson casualties and equipment losses from named streams.
4. Cap casualties by current platoon strength and sink personnel losses through the ledger.
5. Consume artillery rounds for area fire through the ledger.
6. Add tests for deterministic same-seed state, capped casualties, and residual emptiness.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Combat/*.cs`
- `src/Panopticon.Simulation.WesternFront/Systems/AggregateCombatSystem.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/AggregateCombatStochasticTests.cs`

## Acceptance criteria

- [ ] Combat resolves per platoon, not per battalion.
- [ ] Same seed and state produce identical state frames.
- [ ] Artillery rounds and casualties are conserved through ledger sinks.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~AggregateCombatStochastic"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No mean-field variant.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
