# Task Card `WI-A4-26`: Implement the neural commander policy

| Field | Value |
|---|---|
| Card ID | `WI-A4-26` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3e first per-rank neural networks |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-24` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Implement the bounded-squad commander network and decode its outputs into soldier goals.

## Context to read (and only this)

- `docs/roadmap/deep-dives/hierarchical-neural-network-policies.md`: commander contract.
- Landed `ICommanderDecisionPolicy` and rule-based implementation.
- `NeuralNetworkAIDecisionPolicy.cs`: inference implementation pattern.

## Steps

1. Add the neural commander implementation with validated weights.
2. Encode stable member slots with documented padding and overflow behavior.
3. Decode goal outputs without actuating engine orders.
4. Add hand-computed, padding, overflow, tie, and invalid-weight tests.

## Files likely to change

- New neural policy under `src/Panopticon.Simulation/Logic/AI/`
- New tests under `test/Panopticon.Simulation.Tests/Logic/AI/`

## Acceptance criteria

- [ ] Member ordering, padding, and bounds match the design.
- [ ] A hand-computed fixture matches exactly.
- [ ] Output contains goals only for eligible live members.
- [ ] Invalid genome length throws.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NeuralCommanderDecisionPolicy"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No soldier/general implementation or training.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
