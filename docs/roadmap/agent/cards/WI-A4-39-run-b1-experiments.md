# Task Card `WI-A4-39`: Run configured B1 experiments

| Field | Value |
|---|---|
| Card ID | `WI-A4-39` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): experiment harness |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-38`; B1 runner implemented |
| Diagrams | A4 harness diagrams created by `WI-A4-37` |

## Goal

Adapt the B1 runner so each expanded experiment cell constructs its selected implementations and
executes the configured warmup and samples.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-experiment-harness.md`.
- Experiment definition and registry from `WI-A4-38`.
- B1 runner, hardware context, sample, and result APIs.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/StatisticalRunHarness.cs`.

## Steps

1. Expand experiment axes into stable keyed cells.
2. Resolve all bindings through the registry.
3. Invoke B1 warmup and sample execution for each cell.
4. Preserve successful cells when another fails; aggregate a failing exit status.
5. Add fake-runner tests for counts, keys, binding isolation, and failures.

## Files likely to change

- Experiment runner files
- Focused tests

## Acceptance criteria

- [ ] Cell count equals the cartesian product after constraints.
- [ ] Every sample carries cell, implementation, scenario, seed, and hardware identity.
- [ ] Failed cells are explicit and do not erase successful results.
- [ ] No benchmark mechanics are reimplemented outside B1.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExperimentRunner"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No report visualization or performance threshold tuning.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-40`.
