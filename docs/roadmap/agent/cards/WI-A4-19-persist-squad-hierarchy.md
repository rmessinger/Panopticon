# Task Card `WI-A4-19`: Persist squad hierarchy state

| Field | Value |
|---|---|
| Card ID | `WI-A4-19` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3a rank and command hierarchy |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A4-18` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Persist squad membership and commander identity through the existing AI save path with an explicit
versioned layout.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-hierarchical-ai.md`: persistence decision.
- `src/OpenSage.Game/Logic/AI/AIPlayer.cs` and `SkirmishAIPlayer.cs`.
- New squad model from `WI-A4-18`.
- Existing AI persistence tests and `StatePersister` conventions.

## Steps

1. Add the designed hierarchy persistence block, calling `PersistVersion()` first.
2. Resolve object identities only after required player/object state exists.
3. Preserve saves without hierarchy data according to the design.
4. Add round-trip and missing-member tests.

## Files likely to change

- Hierarchy persistence files under `src/OpenSage.Game/Logic/AI/`
- Existing or new AI persistence tests

## Acceptance criteria

- [ ] Save/load preserves squad IDs, membership, and commanders.
- [ ] Missing dead objects do not create dangling references.
- [ ] The version behavior is explicit and tested.
- [ ] Existing AI persistence tests remain green.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~AIPlayerPersist|FullyQualifiedName~Squad"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not persist network weights unless the grounded design explicitly assigns them here.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
