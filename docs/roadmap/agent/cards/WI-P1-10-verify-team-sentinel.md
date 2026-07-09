# Task Card: `WI-P1-10` — Verify/fix team sentinel for default (FFA) slots

| Field | Value |
|---|---|
| Card ID | `WI-P1-10` |
| Roadmap phase | [Track P · P1 — Skirmish setup & player relationships](../../track-p-playable-baseline.md) (sub-phase P1-1, [deep dive §3.1](../../deep-dives/p1-skirmish-setup.md)) |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-P1-02` |
| Diagrams | [P1 Architecture Diagrams](../../deep-dives/p1-skirmish-setup.md#8-architecture-diagrams) |

## Goal

Confirm the relationship-wiring "no team" sentinel is consistent end-to-end, and fix it if not.
`PlayerManager.OnNewGame` treats two players as allies when `left.Team == right.Team && left.Team != -1`,
i.e. **only `-1` means "no team / free-for-all"**. But `SkirmishSlot.Team` is an `sbyte` that
**defaults to `0`**, and `0` corresponds to GUI option `Team:1` (a real team), not "no team". If AI
slots are added without the team combo ever being set, every untouched slot shares `Team == 0` and is
wired as **mutual allies** — turning an intended 1v1/FFA into an all-allied non-game. Verify the real
default and the GUI mapping, then make the sentinel correct.

## Context to read (and only this)

- `src/OpenSage.Game/Logic/PlayerManager.cs:24-90` — `OnNewGame(...)`: the
  `left.Team == right.Team && left.Team != -1` ally/enemy wiring and the `_players[i+2].Team = playerSettings[i].Team`
  copy.
- `src/OpenSage.Game/Network/SkirmishSlot.cs:98-110` — `Team` is `sbyte`, backing field `_team`
  default `0` (no explicit initializer).
- `src/OpenSage.Mods.Generals/Gui/GameOptionsUtil.cs:57-66` — the `ComboBoxTeam` fill: labels
  `Team:0..Team:4` map to data items `(sbyte)-1, 0, 1, 2, 3`. So GUI `Team:0` (the "no team" entry)
  → `-1`, and data value `0` is GUI `Team:1` (a real team).
- `src/OpenSage.Mods.Generals/Gui/GameOptionsUtil.cs:210-235` — slot reset (`slot.Team = 0`) when a
  slot is opened/closed, and `slot.Team = (sbyte)dataItem` when the combo changes.
- `src/OpenSage.Game/Network/SkirmishManager.cs:415-426` — host disconnect reset also sets
  `slot.Team = 0`.

## Steps

1. Determine the real default team value reaching `OnNewGame` for an AI slot the user never
   edits: trace `SkirmishSlot.Team` (default `0`) → `PlayerSetting.Team` (`SkirmishManager.StartGame`)
   → `_players[i+2].Team`. Confirm whether an unedited slot arrives as `0` (real team `Team:1`) or
   `-1` (no team).
2. If unedited slots arrive as `0`, this is a bug: two unedited AI opponents become allies. Decide the
   fix and apply the smallest one:
   - **Preferred:** default `SkirmishSlot.Team` (and/or the GUI's initial selected index for the team
     combo) to the "no team" value `-1`, so unedited slots are FFA (everyone enemies), matching
     stock Generals behaviour where unassigned-team players fight each other.
   - Confirm the change doesn't break the GUI round-trip (`GameOptionsUtil.cs:319-321` reads
     `slot.Team` back into the combo selection) or the network reset paths.
3. If unedited slots already arrive as `-1` (no bug), record "no gap found — default team is -1"
   in Notes and set the card `Done` without code changes.
4. Whatever the outcome, ensure the invariant: **players with no explicit team are mutual enemies,
   players sharing an explicit team are allies.**

## Files likely to change

- `src/OpenSage.Game/Network/SkirmishSlot.cs` — default team value (if the bug exists).
- `src/OpenSage.Mods.Generals/Gui/GameOptionsUtil.cs` — initial team-combo selection / reset value
  (if needed to keep GUI and data consistent).
- This card file (Notes) if no gap is found.

## Acceptance criteria

- [ ] The default team for an unedited AI slot is documented (value + GUI meaning).
- [ ] Two unedited skirmish opponents end up as **mutual enemies**, not allies, after `OnNewGame`.
- [ ] Two slots explicitly set to the same team are mutual allies; different teams are enemies.
- [ ] GUI team-combo round-trip still selects the correct entry for each slot.
- [ ] Standard validation passes (`dotnet build src`, `dotnet test src --filter Category!=Interactive`,
      `dotnet format src --verify-no-changes`).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not redesign the team model or the relationship API — only correct the default/sentinel.
- If the GUI default index and the data default disagree in a way that needs a design call,
  mark `Blocked` and escalate to [deep dive §3.1](../../deep-dives/p1-skirmish-setup.md) rather than
  guessing.

## On completion

- Set Status to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- If a fix was made, extend `WI-P1-06` boot-validation to assert FFA-default opponents are enemies.

## Result

Confirmed the bug: `SkirmishSlot.Team` (`sbyte`) had no explicit initializer, so it defaulted to
`0`, and every reset path (`GameOptionsUtil.cs` state-change reset, `SkirmishManager.cs` disconnect
reset) also explicitly reset it to `0`. `0` is GUI `Team:1` (a real team), not the "no team" `-1`
sentinel that `PlayerManager.OnNewGame` checks for (`left.Team == right.Team && left.Team != -1`).
So two unedited AI opponents (e.g. two "Easy Army" slots nobody assigned a team to) shared
`Team == 0` and were wired as mutual allies instead of enemies.

Fix applied:
- `SkirmishSlot._team` now defaults to `-1` ("no team").
- `GameOptionsUtil.cs` state-change reset and `SkirmishManager.cs` disconnect reset now reset
  `slot.Team` to `-1` instead of `0`.
- Found and fixed a related bug while validating the GUI round-trip criterion: the team-combo
  `SelectedIndex` lookup in `GameOptionsUtil.UpdateUI` used `slot.Team - 1` (copied from the
  1-based `ColorIndex`/`FactionIndex` round-trip lines above it), but the team combo's
  `DataItem`s already match `slot.Team` directly (`{-1, 0, 1, 2, 3}`), so the `-1` offset picked
  the wrong entry (and would fail to find any entry for the new `-1` default). Changed the lookup
  to compare `slot.Team` directly.

Added tests: `SkirmishSlotTests.DefaultTeam_IsNoTeamSentinel` (new file) and
`PlayerManagerTests.OnNewGame_UnteamedPlayersAreMutualEnemies`. Validation:
`dotnet build src` clean, `dotnet test src --filter Category!=Interactive` 668 passed / 22 skipped
/ 0 failed, `dotnet format src --verify-no-changes` reports 0 CHARSET/style errors.
