# Task Card `WI-A4-22`: Add the general policy and hierarchy adapter

| Field | Value |
|---|---|
| Card ID | `WI-A4-22` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3d general directive interface |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-21` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Add the general-level interface and reference policy, then compose all ranks behind
`IAIDecisionPolicy` without changing `AIPlayer`.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-hierarchical-ai.md`: A4-3d flow.
- `src/OpenSage.Game/Logic/AI/IAIDecisionPolicy.cs`, `AIPlayer.cs`.
- New squad, soldier, and commander contracts.
- `AIDecisionPolicyType.cs` and `AIDecisionPolicyFactory.cs`.

## Steps

1. Add the general context, directive interface, and rule-based policy.
2. Add `HierarchicalAIDecisionPolicy` with the designed per-rank cadences.
3. Compose directives to goals to leaf decisions.
4. Register the hierarchy only through `AIDecisionPolicyFactory`.
5. Keep final order dispatch in the single actuation owner selected by `WI-A4-17`.

## Files likely to change

- New hierarchy files under `src/OpenSage.Game/Logic/AI/`
- `AIDecisionPolicyType.cs`
- `AIDecisionPolicyFactory.cs`
- Focused tests

## Acceptance criteria

- [ ] `AIPlayer` calls the hierarchy through `IAIDecisionPolicy`.
- [ ] Each rank runs at its documented cadence.
- [ ] Callers do not branch on hierarchical policy type.
- [ ] Exactly one rank/path actuates orders.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~HierarchicalAIDecisionPolicy|FullyQualifiedName~AIDecisionPolicyFactory"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No neural rank implementation or training.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-23`.
