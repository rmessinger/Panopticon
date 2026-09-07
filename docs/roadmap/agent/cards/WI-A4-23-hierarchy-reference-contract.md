# Task Card `WI-A4-23`: Lock the hierarchy reference contract

| Field | Value |
|---|---|
| Card ID | `WI-A4-23` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3a through A4-3d |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-A4-19`, `WI-A4-22` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Prove the reference hierarchy repairs commander loss, preserves save state, and sends orders through
the same processing path as the flat policy.

## Context to read (and only this)

- Hierarchy implementation from `WI-A4-18` through `WI-A4-22`.
- Existing `AIPlayer` dispatch tests.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs`.
- Existing mocked skirmish scenario helpers.

## Steps

1. Build a controlled player with two squads.
2. Tick general, commander, and soldier cadences and assert the full data flow.
3. Destroy one commander and assert deterministic promotion and continued orders.
4. Save/reload mid-flow and assert hierarchy identity.
5. Spy at the existing order-dispatch boundary to prove no direct mutation path.

## Files likely to change

- New integration tests under `src/OpenSage.Game.Tests/Logic/AI/`

## Acceptance criteria

- [ ] Full rank flow produces expected orders.
- [ ] Commander death causes no command gap beyond the documented cadence.
- [ ] Save/load preserves the hierarchy.
- [ ] Every concrete order reaches the existing dispatch seam exactly once.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~HierarchyReferenceContract"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not claim behavioral parity with every flat-AI scenario; test the documented contract.

## On completion

- Set Status above to `Done` or `Blocked`.
- Mark A4-3a through A4-3d complete in the deep dive and checklist.
- Append a result line to `.exchange`.
