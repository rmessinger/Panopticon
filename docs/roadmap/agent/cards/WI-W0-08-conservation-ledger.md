# Task Card `WI-W0-08`: Add the conservation ledger

| Field | Value |
|---|---|
| Card ID | `WI-W0-08` |
| Roadmap phase | [Track W · W0](../../track-w-western-front.md): charter fixture |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-07` |
| Diagrams | [W0 §12](../../deep-dives/w0-charter-fixture.md#12-architecture-diagrams) |

## Goal

Make `WesternFrontLedger` the only mutation path for conserved fields and report real conservation residuals.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §9.
- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §4.
- `src/Panopticon.Simulation.WesternFront/State/` and `WesternFrontDomainModel.cs`.

## Steps

1. Add `WesternFrontLedger` with `Move(family, fromKey, toKey, amount)`,
   `Produce(family, side, amount)`, and `Sink(family, fromKey, sinkField, amount)`; resolve keys
   through the registry and scenario; reject negative amounts, negative results, family mismatch,
   and cross-side moves with `InvalidOperationException`.
2. Keep an ordered `LedgerEntry` list per operational tick for diagnostics.
3. Replace the placeholder `conservation-residual` metrics with `DomainConservation.Residuals`
   against the run's first captured frame.
4. Tests: formation to squad, depot to formation, squad to the `killed` sink, and produce each leave
   residuals empty; an invalid operation throws and changes nothing.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/State/WesternFrontLedger.cs`
- `src/Panopticon.Simulation.WesternFront/WesternFrontDomainModel.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontLedgerTests.cs`

## Acceptance criteria

- [ ] Failed operations leave every holding unchanged.
- [ ] Residuals stay empty across a scripted sequence of every operation type.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontLedger"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No gameplay rule decides amounts here; callers supply them.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-W0-09`.
