# Task Card `WI-A4-12`: Carry AI-policy configuration into player construction

| Field | Value |
|---|---|
| Card ID | `WI-A4-12` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): AI-policy experiment follow-on |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A4-11` |
| Diagrams | [Pluggable AI §8](../../deep-dives/a4-pluggable-ai.md#8-architecture-diagrams) |

## Goal

Carry a typed policy payload, including neural-network weights, from player configuration to the
existing factory without changing rule-based defaults.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-ai-policy-evolution.md`.
- `src/OpenSage.Game/Logic/PlayerSettings.cs`, `PlayerManager.cs`, and `Player.cs`.
- `src/OpenSage.Game/Logic/AI/AIDecisionPolicyFactory.cs`.
- Existing AI policy factory and player-manager tests.

## Steps

1. Add the immutable policy-configuration type defined by `WI-A4-11`.
2. Replace the enum-only path with the typed configuration through player construction.
3. Preserve `RuleBased` as the default when no explicit payload exists.
4. Validate neural genome length at the existing factory boundary.
5. Add tests for default, neural, missing-payload, and clone/copy behavior.

## Files likely to change

- `src/OpenSage.Game/Logic/AI/`
- `src/OpenSage.Game/Logic/PlayerSettings.cs`
- `src/OpenSage.Game/Logic/PlayerManager.cs`
- Focused tests under `src/OpenSage.Game.Tests/Logic/AI/`

## Acceptance criteria

- [ ] A 125-float payload reaches `NeuralNetworkAIDecisionPolicy`.
- [ ] Default slots still construct the rule-based policy.
- [ ] Missing or invalid neural payloads fail explicitly.
- [ ] Policy implementations remain factory-only constructions.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~AIDecisionPolicyFactory|FullyQualifiedName~PlayerManager"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not add population or fitness logic.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
