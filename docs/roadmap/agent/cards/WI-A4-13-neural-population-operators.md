# Task Card `WI-A4-13`: Add neural population and variation operators

| Field | Value |
|---|---|
| Card ID | `WI-A4-13` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): AI-policy experiment follow-on |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-11` |
| Diagrams | [Pluggable AI §8](../../deep-dives/a4-pluggable-ai.md#8-architecture-diagrams) |

## Goal

Implement deterministic genome initialization, selection, elitism, mutation, and any designed
crossover independently of match execution.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-ai-policy-evolution.md`.
- `docs/roadmap/deep-dives/neural-network-ai-policy.md`: 125-float genome contract.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/StatisticalRunHarness.cs`: seeded-service style.

## Steps

1. Add immutable genome and population models.
2. Implement the exact seeded operators from the design.
3. Reject invalid genome size and invalid population settings.
4. Add deterministic hand-computed and same-seed tests.

## Files likely to change

- New models and services under `src/OpenSage.Tools.ReplaySketch.Core/`
- New tests under `src/OpenSage.Tools.ReplaySketch.Tests/`

## Acceptance criteria

- [ ] Same inputs and seed produce byte-identical next-generation genomes.
- [ ] Elites are preserved exactly.
- [ ] Mutation and selection match hand-computed fixtures.
- [ ] Every genome remains exactly 125 floats.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~EvolutionOperator"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No game construction, fitness scoring, or UI.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
