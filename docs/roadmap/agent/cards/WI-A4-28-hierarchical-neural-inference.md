# Task Card `WI-A4-28`: Prove hierarchical neural inference

| Field | Value |
|---|---|
| Card ID | `WI-A4-28` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3e first per-rank neural networks |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-A4-25`, `WI-A4-26`, `WI-A4-27` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Run supplied general, commander, and soldier genomes through the complete hierarchy adapter and
assert the final orders.

## Context to read (and only this)

- All per-rank neural policies.
- `HierarchicalAIDecisionPolicy`.
- Existing full-dispatch tests for `NeuralNetworkAIDecisionPolicy`.
- The hand-computed integration fixture in the hierarchical network design.

## Steps

1. Construct one player, squad, commander, and two soldiers.
2. Supply the three designed fixture genomes.
3. Tick through every due rank and assert directives, goals, and final orders.
4. Assert a rank not due at the current cadence retains the documented state.
5. Assert factory construction and save behavior.

## Files likely to change

- New integration test under `src/OpenSage.Game.Tests/Logic/AI/`

## Acceptance criteria

- [ ] The full three-rank fixture produces the expected orders.
- [ ] Cadence gating is explicit and tested.
- [ ] Each supplied genome is used only by its rank.
- [ ] Orders pass through the existing dispatch path once.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~HierarchicalNeuralInference"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No evolution or policy-quality assertion.

## On completion

- Set Status above to `Done` or `Blocked`.
- Mark A4-3e complete.
- Append a result line to `.exchange`.
