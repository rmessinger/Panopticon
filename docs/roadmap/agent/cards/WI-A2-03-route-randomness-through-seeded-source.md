# Task Card `WI-A2-03`: Route simulation-affecting randomness through the seeded source

| Field | Value |
|---|---|
| Card ID | `WI-A2-03` |
| Roadmap phase | [Track A · A2](../../track-a-foundations.md): Reproducibility & statistical stability |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A2-01`, `WI-A2-02`, plus an A2 deep dive with populated architecture diagrams (the [agent guide](../README.md#how-to-maintain-this-guide) gate) |
| Diagrams | Blocked on the A2 deep dive. Do not mark this card `Ready` until its Architecture Diagrams section exists and is not a placeholder. |

## Goal

Convert every simulation-affecting `uncontrolled` randomness source found by `WI-A2-01` to draw
from the seeded `GameLogic.Random`, so a run's random stream is fully determined by its seed.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a2-reproducibility.md`: the `uncontrolled` and
  simulation-affecting rows. These are the exact and only call sites in scope.
- The A2 deep dive's architecture diagrams: the target seeded-source shape.
- `src/OpenSage.Mathematics/IRandom.cs`: the interface to draw from.
- `src/OpenSage.Game/Logic/GameLogic.cs:60-75`: the logic-side seeded instance.

## Steps

1. For each simulation-affecting `uncontrolled` site in the audit, replace its private `Random` with
   an `IRandom` supplied from `GameLogic.Random`. Thread it through as a constructor parameter; do
   not add a static or ambient accessor.
2. Leave presentation-only sources alone, including `ParticleSystemUtility` and `AudioSystem`,
   unless the audit classified them as simulation-affecting.
3. Do not change draw order or the number of draws at any converted site. Reproducibility depends on
   the stream being stable, so an extra or reordered draw is a defect even if the distribution is
   unchanged.
4. Do not change `IRandom`, `SystemRandom`, or the `Seed` semantics.
5. Add a test that runs the same scenario twice with the same seed through the P0 harness and
   asserts the key metrics match, per the tolerance the A2 deep dive defines.
6. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- The exact files named in the audit's simulation-affecting `uncontrolled` rows.
- `src/OpenSage.Game.Tests/` (new test file)

## Acceptance criteria

- [ ] No simulation-affecting code path constructs its own `Random` or uses `Random.Shared`.
- [ ] Two same-seed runs of one scenario agree on the key metrics within the deep dive's tolerance.
- [ ] The P7 regression suite passes unchanged:
      `dotnet test src --filter "FullyQualifiedName~P7_ScenarioMatrixTests"`.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P7_ScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not attempt bit-exact determinism or a strict-hash mode. The strict-hash mode is opt-in and
  belongs to the verification harness, `WI-A2-04`.
- Do not fix non-random nondeterminism from `WI-A2-02` here.
- Do not change save or replay format to carry the seed unless the audit found it is already
  missing and the deep dive specifies the field.
- If threading `IRandom` into a site requires changing a public API or the module parse tables, stop
  and mark `Blocked`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the A2 "centralize all randomness through a seeded source" clause in
  [`implementation-checklist.md`](../../implementation-checklist.md).
- Change `WI-A2-04` from `Blocked` to `Ready`.
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
