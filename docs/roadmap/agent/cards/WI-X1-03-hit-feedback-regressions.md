# Task Card `WI-X1-03`: Hit-feedback regression coverage

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X1-03` |
| Roadmap phase | [X1](../../track-x-fixes-tweaks.md) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-X1-02` |
| Diagrams | [Architecture](../../deep-dives/x1-imported-unit-hit-feedback.md#architecture-diagrams) |

## Goal

Prove effects are presentation-only and reuse the existing damage lifecycle, target position, and throttle.

## Context to read (and only this)

- [Audit/results](../../implementation/track-x-x1-hit-feedback.md#audit-and-results).
- `UnitDamageEffectPolicyTests.cs` from `WI-X1-02`; `MockedGameTest` and `FXNugget.Execute` provide a graphics-free effect recorder seam.
- `src/Panopticon.Simulation/Logic/Object/Body/ActiveBody.cs`: damage processing and `DoDamageFX`.
- Existing unit-content materialization/publication tests under `test/Panopticon.Simulation.Tests/Content/Importing/Units/`.

## Steps

1. Add synthetic coverage for opted-in binding, absence, missing references, and successful/failed publication leaving native identities unchanged.
2. Using the existing test seam or effect recorder, assert a real weapon hit selects the target effect and position, not the attacker's muzzle; repeated hits respect authored throttling.
3. Compare configured and unconfigured runs for health loss, damage count, and death outcome. Check headless execution needs no graphics resources.
4. If no bounded effect-observation seam exists, record the gap and defer visual assertions to X1-4 rather than add a general event system.

## Files likely to change

- Focused tests in `test/Panopticon.Simulation.Tests/Content/Importing/Units/` and the audit-approved existing damage-effect test seam.
- `docs/roadmap/implementation/track-x-x1-hit-feedback.md`.

## Acceptance criteria

- [x] Policy and publication cases are covered, including unchanged native definitions.
- [x] Health/damage/death results agree with and without presentation mapping.
- [x] Target placement and throttle have test evidence, or an explicit visual-evidence handoff without a false test-pass claim.
- [x] Standard validation passes or independently reproduced base failures are reported.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not alter damage processing to simplify assertions. Stop and preserve golden/determinism deviation artifacts for the operator.

## On completion

Set Done, tick X1-3, update progress and `.exchange`, and reconcile X1-4 with evidence requirements.
