# Task Card `WI-A4-40`: Write the experiment comparison report

| Field | Value |
|---|---|
| Card ID | `WI-A4-40` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): experiment harness |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-39` |
| Diagrams | A4 harness diagrams created by `WI-A4-37` |

## Goal

Aggregate B1 samples into the versioned A5-consumable experiment report with implementation,
statistics, hardware, and artifact metadata.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-experiment-harness.md`: report schema.
- Experiment cell results from `WI-A4-39`.
- B1 statistical and hardware result types.
- `docs/roadmap/deep-dives/a5-visualization.md`: matrix consumer.

## Steps

1. Add the report DTOs and version.
2. Include bindings, implementation identities, samples, aggregate statistics, hardware context,
   failures, and relative artifact references.
3. Write JSON and CSV forms where required by the design.
4. Add round-trip, portability, partial-failure, and compatibility tests.

## Files likely to change

- Experiment report files
- Focused tests

## Acceptance criteria

- [ ] A report moved with its artifact directory still resolves relative references.
- [ ] Mean, variance/confidence interval, samples, and hardware context are preserved.
- [ ] Partial failures remain machine-readable.
- [ ] Unsupported report versions fail clearly.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExperimentReport"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No MAUI view or benchmark execution changes.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-41`.
