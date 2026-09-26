# Task Card `WI-B7-04`: Generalize W2 group aggregation into a reusable contract

| Field | Value |
|---|---|
| Card ID | `WI-B7-04` |
| Roadmap phase | [Track B · B7](../../track-b-scale.md): variable simulation fidelity |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | all WI-W2 cards; `WI-B7-01` |
| Diagrams | Draft: flip to Ready once prerequisite cards are Done and the named types are reconciled. |

## Goal

Extract a reusable aggregation contract from the W2 promotion and reconciliation evidence.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: W2 concrete promotion and demotion model.
- `docs/roadmap/deep-dives/b7-variable-fidelity.md` §5: reusable aggregation contract.

## Steps

1. Define aggregate identity, member ledger, materialization, reconciliation, and transition-record interfaces.
2. Map the W2 soldier promotion contract onto those interfaces.
3. Add tests with a synthetic aggregate that materializes and reconciles members.
4. Keep W2-specific formation and casualty rules outside the common contract.

## Files likely to change

- `src/Panopticon.Simulation/Fidelity/Aggregation/`
- `test/Panopticon.Simulation.Tests/Fidelity/`

## Acceptance criteria

- [ ] Aggregates materialize one or more `GameObject`s.
- [ ] Reconciliation preserves declared quantities.
- [ ] Transition records include reason, inputs, and moved quantities.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not move W2 domain rules into core simulation.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
