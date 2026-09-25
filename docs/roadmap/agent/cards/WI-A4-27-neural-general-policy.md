# Task Card `WI-A4-27`: Implement the neural general policy

| Field | Value |
|---|---|
| Card ID | `WI-A4-27` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3e first per-rank neural networks |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-24` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Implement the bounded squad-summary general network and decode its outputs into commander
directives.

## Context to read (and only this)

- `docs/roadmap/deep-dives/hierarchical-neural-network-policies.md`: general contract.
- Landed `IGeneralDecisionPolicy` and rule-based implementation.
- `NeuralNetworkAIDecisionPolicy.cs`: inference implementation pattern.

## Steps

1. Add the neural general implementation with validated weights.
2. Encode stable squad slots with documented padding and overflow behavior.
3. Decode directives without soldier goals or engine orders.
4. Add hand-computed, padding, overflow, tie, and invalid-weight tests.

## Files likely to change

- New neural policy under `src/Panopticon.Simulation/Logic/AI/`
- New tests under `test/Panopticon.Simulation.Tests/Logic/AI/`

## Acceptance criteria

- [ ] Squad ordering, padding, and bounds match the design.
- [ ] A hand-computed fixture matches exactly.
- [ ] Output contains directives only for eligible squads.
- [ ] Invalid genome length throws.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NeuralGeneralDecisionPolicy"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No soldier/commander implementation or training.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
