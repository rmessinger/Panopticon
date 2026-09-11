# Task Card `WI-A4-18`: Add squad model and lifecycle

| Field | Value |
|---|---|
| Card ID | `WI-A4-18` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3a rank and command hierarchy |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-17` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Add stable squad identity, membership, commander selection, spawn assignment, death promotion, and
empty-squad cleanup.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-hierarchical-ai.md`: A4-3a contract.
- `src/OpenSage.Game/Logic/Team.cs`, `Player.cs`, and `GameLogic.cs`.
- `src/OpenSage.Game/Logic/Object/GameObject.cs`: identity and lifecycle.
- Existing object ownership and destruction tests.

## Steps

1. Add the squad identifiers and manager named by the design.
2. Derive initial membership from the chosen simulation-owned grouping.
3. Handle spawn, transfer, death, promotion, and empty-squad removal.
4. Apply the documented stable commander ordering and tie-break.
5. Add lifecycle tests.

## Files likely to change

- New hierarchy files under `src/OpenSage.Game/Logic/AI/`
- Narrow lifecycle integration in `src/OpenSage.Game/Logic/`
- New tests under `src/OpenSage.Game.Tests/Logic/AI/`

## Acceptance criteria

- [ ] Each eligible object belongs to at most one squad.
- [ ] Commander death promotes exactly the documented survivor.
- [ ] Ownership transfer and empty squads leave no stale membership.
- [ ] Stable identifiers do not depend on collection iteration order.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Squad"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No policy interfaces, networks, or UI.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-19` and `WI-A4-20`.
