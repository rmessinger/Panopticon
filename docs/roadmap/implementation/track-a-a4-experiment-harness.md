# Implementation plan: Track A · A4 experiment harness

> Audience: human + agent implementation plan for the A4 experiment harness chain.

Source of truth: [a4-experiment-harness deep dive](../deep-dives/a4-experiment-harness.md) and
[Architecture diagrams](../deep-dives/a4-experiment-harness.md#architecture-diagrams).

## Scope

Typed experiment definitions over the implementation registry, per-cell B1 execution, the
experiment report envelope, and the two-implementation comparison milestone with CLI wiring.
B1 owns benchmark mechanics; this chain owns selection and orchestration.

## Card sequence

| Card | Output | Status |
|---|---|---|
| [`WI-A4-37`](../agent/cards/WI-A4-37-design-experiment-harness.md) | Harness design deep dive | Ready |
| [`WI-A4-38`](../agent/cards/WI-A4-38-experiment-variant-registry.md) | Definition and registry types | Blocked (WI-A4-37) |
| [`WI-A4-39`](../agent/cards/WI-A4-39-run-b1-experiments.md) | Per-cell B1 execution | Blocked (WI-A4-38) |
| [`WI-A4-40`](../agent/cards/WI-A4-40-write-experiment-comparison-report.md) | Report envelope and writers | Blocked (WI-A4-39) |
| [`WI-A4-41`](../agent/cards/WI-A4-41-compare-two-seam-implementations.md) | Comparison milestone and CLI | Blocked (WI-A4-40) |

## Acceptance by card

| Card | Acceptance criteria |
|---|---|
| WI-A4-38 | Experiment definitions round-trip JSON; all implementations construct through the registry; invalid bindings fail before any benchmark; registry identity is stable and versioned. |
| WI-A4-39 | Axis expansion produces stable keyed cells; each cell runs B1 warmup and measurement; failed cells are explicit without erasing successful ones. |
| WI-A4-40 | Reports carry bindings, identities, samples, aggregates, hardware context, and failures; artifact references are relative; unsupported schema versions fail clearly. |
| WI-A4-41 | One two-cell experiment differs only in the swept seam binding, produces one A5-compatible report with no caller branching, and the harness checklist item is ticked. |

## Validation

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```
