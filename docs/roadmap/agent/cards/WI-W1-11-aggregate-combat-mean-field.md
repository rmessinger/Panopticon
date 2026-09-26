# Task Card `WI-W1-11`: Add mean-field aggregate combat variant

| Field | Value |
|---|---|
| Card ID | `WI-W1-11` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-10` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-10` is Done.

Add deterministic mean-field combat using fixed-point personnel accumulators.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §3 and §8.2.
- Stochastic combat from `WI-W1-10`.

## Steps

1. Add `CombatVariant` selection to `AggregateCombatSystem`.
2. Add per-formation fixed-point `long` accumulators in units of 1e-6 personnel.
3. Reuse lambda equations and convert whole-person parts to ledger casualties.
4. Persist accumulator state in schema-compatible formation or combat records.
5. Add tests for fractional carry, bit-exact repeatability, and parity with integer cases.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Combat/*.cs`
- `src/Panopticon.Simulation.WesternFront/Systems/AggregateCombatSystem.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/AggregateCombatMeanFieldTests.cs`

## Acceptance criteria

- [ ] Repeated `0.25` expected casualties produces exactly one casualty on the fourth tick.
- [ ] Accumulator values are `long`, not `double` state.
- [ ] Same seed does not affect mean-field results.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~AggregateCombatMeanField"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No new stochastic draws.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
