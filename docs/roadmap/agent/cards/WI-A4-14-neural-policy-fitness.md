# Task Card `WI-A4-14`: Add neural-policy fitness scoring

| Field | Value |
|---|---|
| Card ID | `WI-A4-14` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): AI-policy experiment follow-on |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-11` |
| Diagrams | [Pluggable AI §8](../../deep-dives/a4-pluggable-ai.md#8-architecture-diagrams) |

## Goal

Convert `GameOutcome` and existing T0 metrics into the documented scalar fitness plus diagnostic
components.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-ai-policy-evolution.md`.
- `src/Panopticon.Simulation/Logic/GameOutcome.cs`.
- `src/Panopticon.Simulation/RunOutput.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/StatisticalRunHarness.cs`.

## Steps

1. Add the scorer input and result DTOs from the design.
2. Implement terminal outcome ordering and shaped metric terms.
3. Reject incomplete or incompatible run outputs explicitly.
4. Add fixtures for win, loss, score decision, sudden death, and shaped-term ties.

## Files likely to change

- New scorer under `src/Panopticon.Tools.ReplaySketch.Core/Services/`
- New tests under `test/Panopticon.Tools.ReplaySketch.Tests/`

## Acceptance criteria

- [ ] All terminal outcomes receive the documented ordering.
- [ ] Each shaped term is separately visible in the result.
- [ ] No hidden dependency on render or interactive state.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~AIFitnessScorer"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not add new game metrics to improve fitness.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
