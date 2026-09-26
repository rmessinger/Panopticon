# Task Card `WI-W0-08`: Add the conservation ledger

| Field | Value |
|---|---|
| Card ID | `WI-W0-08` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-07` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Add `WesternFrontLedger` as the only mutation path for conserved fields and expose residual metrics.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §9.
- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §4.
- `src/Panopticon.Simulation.WesternFront/WesternFrontDomainModel.cs` from `WI-W0-07`.

## Steps

1. Add holdings references for pool, depot, link, formation, soldier, and ledger sink fields.
2. Implement `Move`, `Produce`, and `Sink` with negative-result rejection.
3. Add residual metric sampling using `DomainConservation.Residuals` against a baseline frame.
4. Add tests for successful moves, negative rejection, source/sink balancing, and residual emptiness.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/State/WesternFrontLedger.cs`
- `src/Panopticon.Simulation.WesternFront/WesternFrontDomainModel.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontLedgerTests.cs`

## Acceptance criteria

- [ ] Depot to formation to soldier to ledger sink leaves no residual.
- [ ] Attempting to move more than a holding contains throws.
- [ ] Residual metric keys include family and side.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontLedger"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No W1 combat, logistics, or consumption rules.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
