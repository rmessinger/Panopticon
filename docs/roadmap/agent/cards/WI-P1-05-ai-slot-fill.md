# Task Card: `WI-P1-05` — AI slot fill before `StartGame`

| Field | Value |
|---|---|
| Card ID | `WI-P1-05` |
| Roadmap phase | [Track P · P1 — Skirmish setup & player relationships](../../track-p-playable-baseline.md) (sub-phase P1-3, see [deep dive §3.3](../../deep-dives/p1-skirmish-setup.md)) |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-P1-04` |
| Diagrams | [P1 Architecture Diagrams](../../deep-dives/p1-skirmish-setup.md#8-architecture-diagrams) |

## Goal

Verify that AI slots are fully specified (faction, color, start position) before `StartGame`
is called from both the lobby path and any headless / programmatic start path used by the
P0 harness. Close any gaps so that no AI player enters `StartGame` with an unset
`SideName`, `Color`, or `StartPosition`.

## Context to read (and only this)

- `src/OpenSage.Mods.Generals/Gui/GameOptionsUtil.cs:239-305` — `HandleStartButtonClickAsync`:
  fills missing start positions/factions/colors for each slot before calling
  `SkirmishManager.HandleStartButtonClickAsync()`. This is the lobby path — understand exactly
  what it fills and when it skips.
- `src/OpenSage.Game/Network/SkirmishManager.cs:55-93` — `StartGame(...)`: builds
  `List<PlayerSetting>` from `Settings.Slots` and passes to `Game.StartSkirmishOrMultiPlayerGame`.
  Check: does it set default values for empty fields, or rely on the GUI to have done so?
- `src/OpenSage.Game/Logic/PlayerSettings.cs:17-36` — `PlayerSetting` fields: `StartPosition`,
  `SideName`, `Color`, `Team`, `Owner`, `Name`, `IsLocalForMultiplayer`.
- `src/OpenSage.Game/Logic/PlayerManager.cs:50-76` — `CreatePlayers(...)`: how
  `PlayerSetting` values feed into `Player` construction; identify what happens when
  `SideName` or `StartPosition` is unset.
- `src/OpenSage.Mods.Generals/Gui/GameOptionsUtil.cs:68-75` — `GetPlayableSides()`: source
  for random faction assignment.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs` — the harness
  acceptance tests to understand how the P0 harness currently starts a skirmish, so you can
  tell whether it goes through the lobby fill code or bypasses it.

## Steps

1. Trace both start paths (lobby and P0 harness / headless) and determine whether AI slots
   are guaranteed to have `SideName != null`, `Color != default`, and `StartPosition > 0` by
   the time `PlayerManager.CreatePlayers` is called.

2. If the lobby path is complete, note "lobby path OK" in Notes.

3. If the headless / harness path bypasses `GameOptionsUtil` and can reach `CreatePlayers`
   with unset fields, add a `FillMissingSlotDefaults(IGame game, List<PlayerSetting> slots)`
   utility method (or add the logic inline in `SkirmishManager.StartGame`) that:
   - Assigns a random playable side from `Game.GetPlayableSides()` if `SideName` is empty.
   - Assigns the lowest free color if `Color` is default.
   - Assigns the lowest free start-position index if `StartPosition` is 0.
   This should replicate what `GameOptionsUtil.HandleStartButtonClickAsync` already does, so
   consider extracting it into a shared static helper both paths call.

4. Add no new tests (that is `WI-P1-06`). Do not change the lobby GUI flow.

## Files likely to change

- `src/OpenSage.Game/Network/SkirmishManager.cs` — slot defaults in the headless path,
  or a shared helper call (if the gap exists).
- `src/OpenSage.Mods.Generals/Gui/GameOptionsUtil.cs` — extract shared helper (if needed).
- This card file only (append to Notes) if no gap is found.

## Acceptance criteria

- [ ] Both the lobby start path and the P0 harness / headless start path guarantee that every
      AI slot has a non-empty `SideName`, non-default `Color`, and a positive `StartPosition`
      by the time `CreatePlayers` is called.
- [ ] If a gap existed and was fixed: `dotnet build src` exits 0; no tests regress.
- [ ] `dotnet format src --verify-no-changes` passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change faction/color selection logic for the human player — only AI slots.
- Do not modify the WND GUI flow, only the underlying slot-data path.
- If no gap is found, set Status to Done and note "no gap found — both paths already complete."

## On completion

- Set Status to `Done` (or `Blocked` + reason).
- Tick the P1-3 item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- If a shared helper was extracted, note the new type/method name in Notes for `WI-P1-06`.

## Result

Done (commit 677b7bc9, "Add skirmish slot defaulting"). Extracted a shared
`SkirmishSlotDefaults.ApplyDefaults(...)` helper (`src/OpenSage.Game/Network/SkirmishSlotDefaults.cs`)
that assigns a random playable side, a free color, and a free start position to any unset AI slot;
called from `SkirmishManager.StartGame` so both the lobby and headless/programmatic start paths go
through the same defaulting logic. A start-position collision bug in this helper was found and fixed
later by `WI-P1-09`.
