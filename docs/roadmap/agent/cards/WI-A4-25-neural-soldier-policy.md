# Task Card `WI-A4-25`: Implement the neural soldier policy

| Field | Value |
|---|---|
| Card ID | `WI-A4-25` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3e first per-rank neural networks |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-24` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Implement the exact soldier network contract and decode its output through the existing soldier
policy seam.

## Context to read (and only this)

- `docs/roadmap/deep-dives/hierarchical-neural-network-policies.md`: soldier contract.
- Landed `ISoldierDecisionPolicy` and rule-based implementation.
- `NeuralNetworkAIDecisionPolicy.cs`: inference implementation pattern.

## Steps

1. Add the neural soldier implementation with cloned, validated weights.
2. Implement encoding, forward pass, and decoding exactly as documented.
3. Reuse reference-policy target/order helpers where specified.
4. Add hand-computed forward-pass, tie, invalid-weight, and action tests.

## Files likely to change

- New neural policy under `src/OpenSage.Game/Logic/AI/`
- New tests under `src/OpenSage.Game.Tests/Logic/AI/`

## Acceptance criteria

- [ ] All indexed features and actions match the design.
- [ ] A hand-computed fixture matches exactly.
- [ ] Invalid genome length throws.
- [ ] Orders use the existing soldier actuation path.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NeuralSoldierDecisionPolicy"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No commander/general implementation or training.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
