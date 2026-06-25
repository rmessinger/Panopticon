# Task Card: `WI-P1-04` — Unified skirmish spawn path

| Field | Value |
|---|---|
| Card ID | `WI-P1-04` |
| Roadmap phase | [Track P · P1 — Skirmish setup & player relationships](../../track-p-playable-baseline.md) (sub-phase P1-2, see [deep dive §3.2](../../deep-dives/skirmish-setup.md)) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-P1-02` |
| Diagrams | [P1 Architecture Diagrams](../../deep-dives/skirmish-setup.md#8-architecture-diagrams) |

## Goal

Remove the SP/MP spawn divergence at `Game.cs:745-763` so that a single-player skirmish runs
the same start-position lookup, `CreateSkirmishPlayerStartingBuilding`, and camera-at-start path
that the multiplayer branch uses, for every non-neutral player. Replace the ad-hoc
`_players[2]` `LocalPlayer` fallback with an explicit slot-flag read.

## Context to read (and only this)

- `src/OpenSage.Game/Game.cs:717-802` — `StartGame()`: focus on lines 745-763 (the MP-only
  guard for starting buildings + camera) and 722-743 (player loop before the guard).
- `src/OpenSage.Game/Scene3D.cs:430-455` — `CreateSkirmishPlayerStartingBuilding(...)`: takes a
  `Player` and index, looks up `Player{i}_Start` waypoint, creates starting building + units.
- `src/OpenSage.Game/Logic/PlayerManager.cs:39-46` — current `LocalPlayer` selection: falls back to
  `_players[2]` when `isLocalForMultiplayer` not set. This is the ad-hoc indexing to fix.
- `src/OpenSage.Game/Logic/PlayerSettings.cs:17-36` — `PlayerSetting.IsLocalForMultiplayer` flag.
- `src/OpenSage.Game/Network/SkirmishManager.cs:55-93` — how `PlayerSetting.IsLocalForMultiplayer`
  is already set for the human slot in the lobby path.
- `src/OpenSage.Game/Scripting/Waypoints.cs:76-81` — `TryGetPlayerStart(int playerIndex)`.

## Steps

1. Fix `LocalPlayer` selection in `PlayerManager.OnNewGame` (`PlayerManager.cs:39-46`).
   Replace the `_players[2]` positional fallback with: find the player whose corresponding
   `PlayerSetting` has `IsLocalForMultiplayer == true`; if none, fall back to the first
   non-neutral, non-observer human player. Remove any hardcoded `[2]` index.

2. Unify the spawn path in `Game.StartGame` (`Game.cs:745-763`).
   The block inside `if (gameType != GameType.SinglePlayer)` that calls
   `CreateSkirmishPlayerStartingBuilding` and sets the camera should run for any
   skirmish-type game, including SP. Change the guard condition so that it fires for both
   `SinglePlayer` and `MultiPlayer` skirmish starts (not for campaign/other modes).
   A safe condition is: `gameType == GameType.SinglePlayer || gameType == GameType.MultiPlayer`
   (or an `IsSkirmish` predicate if one exists — check `GameType` enum values).

3. Do not alter the relationship setup, player-creation order, or the `SkirmishManager`
   lobby code — only the spawn guard and `LocalPlayer` selection.

## Files likely to change

- `src/OpenSage.Game/Game.cs` — spawn path guard (`~line 745-763`).
- `src/OpenSage.Game/Logic/PlayerManager.cs` — `LocalPlayer` selection (`~line 39-46`).

## Acceptance criteria

- [ ] A `GameType.SinglePlayer` skirmish start calls `CreateSkirmishPlayerStartingBuilding`
      for each non-neutral player, the same as a `MultiPlayer` start.
- [ ] `PlayerManager.LocalPlayer` is set from the `IsLocalForMultiplayer` slot flag, not from
      `_players[2]`.
- [ ] `dotnet build src` and `dotnet test src --filter Category!=Interactive` both pass.
- [ ] `dotnet format src --verify-no-changes` passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not touch relationship setup (`OnNewGame` TODO — that is `WI-P1-02`).
- Do not change how slots are filled (faction/color/start) — that is `WI-P1-05`.
- Do not add new tests here — the boot-validation test is `WI-P1-06`.
- If the `GameType` enum doesn't have a clean skirmish predicate, add a local `bool isSkirmish`
  variable and comment its definition; do not redesign the enum.

## On completion

- Set Status to `Done` (or `Blocked` + reason).
- Tick the P1-2 item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
