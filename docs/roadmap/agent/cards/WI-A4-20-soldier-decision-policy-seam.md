# Task Card `WI-A4-20`: Extract the soldier decision-policy seam

| Field | Value |
|---|---|
| Card ID | `WI-A4-20` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3b soldier-level policy seam |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A4-17`, `WI-A4-18` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Extract the designed soldier decision point with the existing transition logic as its reference
implementation, leaving movement, targeting, and weapon actuation unchanged.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-hierarchical-ai.md`: exact leaf boundary.
- `src/OpenSage.Game/Logic/AI/AIUpdateStateMachine.cs`.
- The state classes named by the implementation plan.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs`.
- Existing tests for those AI states.

## Steps

1. Add `ISoldierDecisionPolicy`, context, goal, and result types from the design.
2. Move only the chosen transition/goal decision into a rule-based policy.
3. Inject it through the documented per-object path.
4. Keep locomotion, target queues, turret behavior, and final order processing unchanged.
5. Add reference-policy equivalence tests.

## Files likely to change

- New policy files under `src/OpenSage.Game/Logic/AI/`
- Only AI state files named by the implementation plan
- Focused AI tests

## Acceptance criteria

- [ ] Rule-based behavior is unchanged for covered states.
- [ ] The policy cannot directly mutate game state.
- [ ] Orders use the existing processing path.
- [ ] No presentation state enters the context.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SoldierDecisionPolicy|FullyQualifiedName~AIUpdate"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not replace locomotion or add a neural policy.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
