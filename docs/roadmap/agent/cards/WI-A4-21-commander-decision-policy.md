# Task Card `WI-A4-21`: Add the commander decision-policy seam

| Field | Value |
|---|---|
| Card ID | `WI-A4-21` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3c commander-level policy |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-20` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Add the commander context and policy interface with a rule-based implementation that translates one
directive into bounded soldier goals.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-hierarchical-ai.md`: A4-3c contract.
- Squad model from `WI-A4-18`.
- Soldier goal and policy types from `WI-A4-20`.
- `src/Panopticon.Simulation/Logic/AI/RuleBasedAIDecisionPolicy.cs`.

## Steps

1. Add the commander interface, context, directive, and goal map types.
2. Implement the designed squad aggregation.
3. Add the rule-based directive-to-goal policy.
4. Enforce the maximum squad size and stable member ordering.
5. Test live, dead, missing, and oversized membership.

## Files likely to change

- New files under `src/Panopticon.Simulation/Logic/AI/`
- New tests under `test/Panopticon.Simulation.Tests/Logic/AI/`

## Acceptance criteria

- [ ] One bounded goal is returned per eligible live member.
- [ ] Dead or missing members receive no goal.
- [ ] Output ordering and truncation are stable.
- [ ] No order is actuated at commander rank.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~CommanderDecisionPolicy"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No general policy, adapter, or neural network.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
