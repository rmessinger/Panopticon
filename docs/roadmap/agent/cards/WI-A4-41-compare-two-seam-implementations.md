# Task Card `WI-A4-41`: Compare two seam implementations end to end

| Field | Value |
|---|---|
| Card ID | `WI-A4-41` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): experiment harness milestone |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-A4-16`, `WI-A4-40` |
| Diagrams | A4 harness diagrams created by `WI-A4-37` |

## Goal

Close A4 by config-selecting rule-based and fixed-genome neural AI policies, running the same B1
scenario, and producing one comparison report without caller changes.

## Context to read (and only this)

- Experiment harness from `WI-A4-38` through `WI-A4-40`.
- AI policy configuration and fixed-genome execution from `WI-A4-12` and `WI-A4-16`.
- B1's smallest reusable scenario fixture.
- `docs/roadmap/implementation-checklist.md`: A4 milestone.

## Steps

1. Define one two-cell experiment differing only in AI policy binding.
2. Run both cells through the same public harness entry point.
3. Assert implementation identities, B1 statistics, outcomes, and artifacts in the report.
4. Assert no benchmark or host caller branches on policy type.
5. Add a CLI or API smoke invocation documented by the A4 implementation plan.

## Files likely to change

- Experiment integration tests
- Narrow CLI/config fixture files
- A4 implementation plan command example

## Acceptance criteria

- [x] Two implementations are selected only by configuration.
- [x] Both run against the same B1 scenario and settings.
- [x] One A5-compatible report compares them.
- [x] No caller change is required between cells.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExperimentHarnessMilestone"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not assert that one policy is faster or better; verify selection and measurement only.

## On completion

- Set Status above to `Done` or `Blocked`.
- Tick the experiment-harness checklist item and set A4 `Done` only if all A4 items are complete.
- Append a result line to `.exchange`.
