# Task Card `WI-A4-15`: Run and report one neural-policy generation

| Field | Value |
|---|---|
| Card ID | `WI-A4-15` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): AI-policy experiment follow-on |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-12`, `WI-A4-13`, `WI-A4-14` |
| Diagrams | [Pluggable AI §8](../../deep-dives/a4-pluggable-ai.md#8-architecture-diagrams) |

## Goal

Evaluate one population across the configured match-seed matrix, rank it, produce the next
population, and emit the designed generation report.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-ai-policy-evolution.md`.
- New policy configuration, population operators, and fitness scorer.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs`.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs`.

## Steps

1. Add an evaluator interface so operator tests remain game-independent.
2. Run each genome against every configured seed and aggregate fitness.
3. Rank genomes, advance the population, and emit best/mean/worst fitness and diversity.
4. Serialize and reload the generation report.
5. Add fake-evaluator tests for ordering, failures, and report round-trip.

## Files likely to change

- New evolution runner and report types under `src/OpenSage.Tools.ReplaySketch.Core/`
- New tests under `src/OpenSage.Tools.ReplaySketch.Tests/`

## Acceptance criteria

- [ ] Exactly population-size × seed-count evaluations occur.
- [ ] Evaluation failure identifies genome and seed without success-shaped fallback.
- [ ] Report values and next-generation ancestry are tested.
- [ ] Report JSON round-trips.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NeuralPolicyEvolution"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No MAUI visualization or hierarchical ranks.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-16`.
