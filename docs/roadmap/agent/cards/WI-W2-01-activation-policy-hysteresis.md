# Task Card `WI-W2-01`: Add activation policies and hysteresis

| Field | Value |
|---|---|
| Card ID | `WI-W2-01` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | all WI-W1 cards; `WI-W0-01`..`WI-W0-09` |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Define `IActivationPolicy`, `ActivationContext`, `ActivationRequest`, and reference policies for W2 activation.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.
- `src/Panopticon.Simulation/SimulationHost.cs:107-180`: scheduler-facing order and frame boundary hooks.

## Steps

1. Add W2 activation policy types under the Western Front domain.
2. Implement `NeverActivatePolicy`, `ContactActivationPolicy`, `OperatorInterestPolicy`, and `AllTacticalActivationPolicy`.
3. Add dwell tracking with default 3 operational ticks.
4. Add unit tests for contested promotion, contact-ended demotion, and no concurrency cap.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Activation/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] ContactActivationPolicy promotes every contested region.
- [ ] Demotion waits until contact ends and 3 operational ticks pass.
- [ ] Transition requests include policy id, reason, and serialized inputs.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No tactical object creation or reconciliation.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
