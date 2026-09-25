# Task Card `WI-A2-04`: Statistical verification harness

| Field | Value |
|---|---|
| Card ID | `WI-A2-04` |
| Roadmap phase | [Track A · A2](../../track-a-foundations.md): Statistical validation |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | A1-5 run metrics and invariants |
| Diagrams | [A2 §5](../../deep-dives/a2-reproducibility.md#5-architecture-diagrams) |

## Goal

Add the A2 verification harness: run one scenario N times through the P0 harness, check every
run's correctness invariants, and report aggregate metric distributions.

## Context to read (and only this)

- `docs/roadmap/open-questions.md`: the determinism-priority Decision.
- The A2 deep dive: the statistical validation model and selected metrics.
- `docs/roadmap/implementation/track-a-a2-reproducibility.md`: both audits.
- The P7 scenario matrix tests under `test/Panopticon.Simulation.Tests/`: the existing multi-scenario
  run-and-assert pattern to replicate. Find them with
  `grep -r P7_ScenarioMatrixTests test/Panopticon.Simulation.Tests`.
- The P0 harness assertion layer under `src/Panopticon.Tools.ReplaySketch.Core/`: reuse its assertion
  primitives instead of writing new ones.

## Steps

1. Add a harness that runs one P0 scenario N times and collects the metric set named by the deep
   dive. Seeds may vary or be explicitly selected as experiment inputs.
2. Check correctness invariants independently for every run. Report the run and invariant on
   failure.
3. Report each metric's mean, variance, confidence interval, and outliers. Do not assert equality
   between runs.
4. Reuse the P0 assertion primitives and the P7 matrix structure. Do not invent a parallel harness;
   the [agent guide guardrails](../README.md#guardrails-do-not-violate) require ReplaySketch and the
   P0 harness to be the validation surface.
5. Keep the default N small enough that the test stays inside the non-interactive suite's runtime
   budget. If it cannot, gate the higher-N run behind a separate filterable trait.
6. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- `test/Panopticon.Simulation.Tests/` (new harness and test file)

## Acceptance criteria

- [ ] N runs of one scenario check all configured per-run invariants.
- [ ] Aggregate metrics report mean, variance, confidence interval, and outliers.
- [ ] No assertion requires same-seed agreement or state-hash parity.
- [ ] The default run stays inside `dotnet test src --filter Category!=Interactive` without a
      noticeable runtime regression.
- [ ] No new assertion primitive duplicates one the P0 layer already provides.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add cross-run equality or deterministic-execution requirements.
- Do not add the A1-5 tiered recording or divergence localization. Those are A1-5 and A5-4.
- If the deep dive does not select a metric, exclude it rather than expanding scope.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the A2 verification-harness clause in
  [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none.
