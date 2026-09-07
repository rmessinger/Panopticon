# Task Card `WI-A4-04`: Implement `NeuralNetworkAIDecisionPolicy` (inference only)

| Field | Value |
|---|---|
| Card ID | `WI-A4-04` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): AI decision policy seam, §3 of [pluggable-ai.md](../../deep-dives/a4-pluggable-ai.md) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-03` (Done): the state encoding / network shape / decoding design must exist and be followed exactly, not reinvented |
| Diagrams | [Pluggable AI §8](../../deep-dives/a4-pluggable-ai.md#8-architecture-diagrams) |

## Goal

Implement `NeuralNetworkAIDecisionPolicy` as a concrete `IAIDecisionPolicy`: a fixed-weight
feed-forward network (weights supplied at construction, no training in this card) that encodes
`AIDecisionContext` per `WI-A4-03`'s design, runs a forward pass, and decodes the output into
`Order`s. Register it in `AIDecisionPolicyFactory` under a new
`AIDecisionPolicyType.NeuralNetwork`. This card is inference-only: evolving/training weights is
explicitly out of scope (separate, later card set per pluggable-ai.md §4/§6).

## Context to read (and only this)

- `docs/roadmap/deep-dives/neural-network-ai-policy.md` (produced by `WI-A4-03`): the exact
  feature vector, network shape, and decoding scheme to implement. Follow it precisely; if
  anything in it is ambiguous or incomplete, stop and mark this card `Blocked` rather than
  filling gaps with new design decisions.
- `src/OpenSage.Game/Logic/AI/IAIDecisionPolicy.cs`, `AIDecisionContext.cs` (from `WI-A4-01`).
- `src/OpenSage.Game/Logic/AI/AIDecisionPolicyType.cs`, `AIDecisionPolicyFactory.cs` (from
  `WI-A4-02`): where to register the new type.
- `src/OpenSage.Game/Logic/AI/RuleBasedAIDecisionPolicy.cs`: the sibling implementation to
  mirror in class placement/style (namespace, visibility conventions).

## Steps

1. Add `src/OpenSage.Game/Logic/AI/NeuralNetworkAIDecisionPolicy.cs` implementing
   `IAIDecisionPolicy`, constructed with a `float[]` weight vector (the "genome"). Implement the
   feature encoder, forward pass, and action decoder exactly per `WI-A4-03`'s design doc.
2. Validate the weight-vector length matches the design doc's stated total parameter count;
   throw a clear exception on mismatch (do not silently truncate/pad).
3. Add `AIDecisionPolicyType.NeuralNetwork` and wire `AIDecisionPolicyFactory` to construct
   `NeuralNetworkAIDecisionPolicy` given a weight vector (factory signature will need to accept
   weights for this branch. Keep the `RuleBased` branch's signature/behavior unchanged).
4. Add a headless unit test that constructs the policy with a hand-computed weight vector and a
   controlled `AIDecisionContext`, then verifies the decoded orders match the design.
6. Run `dotnet format src`; confirm `--verify-no-changes`.

## Files likely to change

- `src/OpenSage.Game/Logic/AI/NeuralNetworkAIDecisionPolicy.cs` (new)
- `src/OpenSage.Game/Logic/AI/AIDecisionPolicyType.cs`
- `src/OpenSage.Game/Logic/AI/AIDecisionPolicyFactory.cs`
- Test file under `src/OpenSage.Game.Tests/Logic/AI/` (new)

## Acceptance criteria

- [ ] `NeuralNetworkAIDecisionPolicy` implements the exact encoding/network/decoding scheme in
      `neural-network-ai-policy.md`. No ad hoc deviation.
- [ ] The hand-computed weights and controlled context decode to the expected `Order`s.
- [ ] Mismatched weight-vector length throws a clear, tested exception rather than silently
      misbehaving.
- [ ] `RuleBased` policy selection/behavior is unaffected by this card.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NeuralNetworkAIDecisionPolicy"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- No training/evolution logic (population, mutation, fitness): that is a separate, later card
  set gated on A1 (headless host) and P3 (`GameOutcome`).
- No persistence/save-load of a running network's weights beyond what `AIPlayer.Persist`
  already owns. Do not invent a new save path.
- Do not deviate from `WI-A4-03`'s design doc; if implementation reveals the design is wrong or
  incomplete, stop, mark `Blocked`, and route the correction back through a design-doc update
  (re-open `WI-A4-03`-style scope) rather than deciding it ad hoc in code.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up: once A1 (headless host) and P3 (`GameOutcome`) land, decompose the evolutionary
  training harness (pluggable-ai.md §4) into its own card set: population management, fitness
  scoring, selection/mutation, per-generation reporting to A5.
