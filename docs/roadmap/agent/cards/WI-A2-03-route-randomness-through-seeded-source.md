# Task Card `WI-A2-03`: Route simulation-affecting randomness through the seeded source

| Field | Value |
|---|---|
| Card ID | `WI-A2-03` |
| Roadmap phase | [Track A · A2](../../track-a-foundations.md): Statistical validation |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A2-01`, `WI-A2-02`, plus an A2 deep dive with populated architecture diagrams (the [agent guide](../README.md#how-to-maintain-this-guide) gate) |
| Diagrams | Blocked on the A2 deep dive. Do not mark this card `Ready` until its Architecture Diagrams section exists and is not a placeholder. |

## Goal

Convert every simulation-affecting `uncontrolled` randomness source found by `WI-A2-01` to draw
from the seeded `GameLogic.Random`, providing a selectable experiment input.

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
3. Preserve draw order and count to keep this refactor behavior-safe.
4. Do not change `IRandom`, `SystemRandom`, or the `Seed` semantics.
5. Add a test that verifies the configured seed reaches `GameLogic.Random` and converted consumers.
6. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- The exact files named in the audit's simulation-affecting `uncontrolled` rows.
- `src/OpenSage.Game.Tests/` (new test file)

## Acceptance criteria

- [ ] No simulation-affecting code path constructs its own `Random` or uses `Random.Shared`.
- [ ] The configured seed reaches `GameLogic.Random` and converted consumers.
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

- Do not add cross-run equality requirements.
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
