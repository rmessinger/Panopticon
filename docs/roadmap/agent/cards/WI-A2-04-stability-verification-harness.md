# Task Card `WI-A2-04`: Stability verification harness with opt-in strict-hash mode

| Field | Value |
|---|---|
| Card ID | `WI-A2-04` |
| Roadmap phase | [Track A · A2](../../track-a-foundations.md): Reproducibility & statistical stability |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-A2-03` (randomness must be seeded before stability can be asserted) |
| Diagrams | Blocked on the A2 deep dive. Do not mark this card `Ready` until its Architecture Diagrams section exists and is not a placeholder. |

## Goal

Add the A2 verification harness: re-run one scenario N times with a fixed seed through the P0
harness and assert the recorded metrics and invariants stay within tolerance, with an opt-in
strict-hash mode for same-platform, same-build debugging.

## Context to read (and only this)

- `docs/roadmap/open-questions.md`: the relaxed-determinism Decision and the A2 strict-hash scope
  Decision. Strict-hash is best-effort and must not be a gating assertion.
- The A2 deep dive: the tolerance model and which metrics are the stability signal.
- `docs/roadmap/implementation/track-a-a2-reproducibility.md`: both audits.
- The P7 scenario matrix tests under `src/OpenSage.Game.Tests/`: the existing multi-scenario
  run-and-assert pattern to replicate. Find them with
  `grep -r P7_ScenarioMatrixTests src/OpenSage.Game.Tests`.
- The P0 harness assertion layer under `src/OpenSage.Tools.ReplaySketch.Core/`: reuse its assertion
  primitives instead of writing new ones.

## Steps

1. Add a harness that runs one P0 scenario N times with the same seed and collects the metric set
   the deep dive names as the stability signal.
2. Assert each metric's spread across runs is within the deep-dive tolerance. Report the actual
   spread on failure, not just a boolean.
3. Add an opt-in strict mode, off by default, that additionally compares a per-frame state hash and
   reports the first differing frame. It must be enabled by an explicit flag and must never run in
   the default test pass.
4. Reuse the P0 assertion primitives and the P7 matrix structure. Do not invent a parallel harness;
   the [agent guide guardrails](../README.md#guardrails-do-not-violate) require ReplaySketch and the
   P0 harness to be the validation surface.
5. Keep the default N small enough that the test stays inside the non-interactive suite's runtime
   budget. If it cannot, gate the higher-N run behind a separate filterable trait.
6. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- `src/OpenSage.Game.Tests/` (new harness and test file)

## Acceptance criteria

- [ ] N same-seed runs of one scenario are asserted within tolerance, with the observed spread
      reported on failure.
- [ ] Strict-hash mode exists, is off by default, and reports the first divergent frame when on.
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

- Do not make strict-hash a gating assertion. Parallelism may legitimately diverge.
- Do not extend the harness across platforms or builds; same-platform, same-build only.
- Do not add the A1-5 tiered recording or divergence localization. Those are A1-5 and A5-4.
- If the deep dive does not specify a tolerance for a metric, exclude that metric and note it,
  rather than picking a number.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the A2 verification-harness clause in
  [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - A card implementing the concurrent-write conflict-resolution rules once the deep dive defines
    them (the remaining A2 checklist item).
