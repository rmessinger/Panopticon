# Task Card `WI-A4-35`: Extract the fidelity-policy seam

| Field | Value |
|---|---|
| Card ID | `WI-A4-35` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): stable fidelity-policy seam |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-10`, `WI-A4-31`; B7-1 contract must be implementation-ready |
| Diagrams | [B7 §8](../../deep-dives/b7-variable-fidelity.md#8-architecture-diagrams) |

## Goal

Add B7's fidelity policy contract with an always-full-fidelity reference that leaves current
scheduling and outcomes unchanged.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b7-variable-fidelity.md`: B7-1 model and policy.
- `docs/roadmap/implementation/track-a-a4-experiment-seams.md`.
- `src/Panopticon.Simulation/Logic/GameLogic.cs`.
- `src/Panopticon.Simulation/Logic/Object/Update/UpdateModule.cs` and sleepy-update types.
- A4-1 domain fidelity hooks.

## Steps

1. Add fidelity level, context, policy, and manager types from B7.
2. Implement and select `FullFidelityPolicy`.
3. Expose the selected level through the domain recording/visualization projection.
4. Add equivalence tests showing unchanged update scheduling and outcomes.

## Files likely to change

- New files under `src/Panopticon.Simulation/Logic/Fidelity/`
- Narrow logic-step and domain-adapter integration
- Focused tests

## Acceptance criteria

- [ ] Every object receives full fidelity under the reference policy.
- [ ] Existing update cadence and outcomes are unchanged.
- [ ] The selected level is observable for recording.
- [ ] No view-driven input enters shared simulation state.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~FidelityPolicy|FullyQualifiedName~SleepyUpdate"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No temporal LOD, simplification, aggregation, precision reduction, or physics transition.

## On completion

- Set Status above to `Done` or `Blocked`.
- Update the fidelity row in `seams.md`.
- Append a result line to `.exchange`.
