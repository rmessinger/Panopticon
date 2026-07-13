# Task Card `WI-P1-09`: Fix start-position collision in `SkirmishSlotDefaults.ApplyDefaults`

| Field | Value |
|---|---|
| Card ID | `WI-P1-09` |
| Roadmap phase | [Track P · P1: Skirmish setup & player relationships](../../track-p-playable-baseline.md) (relates to P1-3, [deep dive §3.3](../../deep-dives/p1-skirmish-setup.md)) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P1-05` |
| Diagrams | [P1 Architecture Diagrams](../../deep-dives/p1-skirmish-setup.md#8-architecture-diagrams) |

## Goal

Fix a regression introduced when slot-default logic was extracted from `GameOptionsUtil` into
`SkirmishSlotDefaults.ApplyDefaults`: the new code builds the available-start-position pool
(`1..NumPlayers`) but never removes positions that are already assigned to other slots before
auto-distributing the remainder. As a result a slot whose `StartPosition` was set manually (or by
another path) can have its position re-handed to a different slot, producing two players on the same
start point. Restore the "reserve already-taken positions first" behaviour the GUI code had.

## Context to read (and only this)

- `src/OpenSage.Game/Network/SkirmishSlotDefaults.cs`: `ApplyDefaults(...)`: the
  `availablePositions` build loop and the per-slot `if (slot.StartPosition == 0)` assignment that
  pops `availablePositions[^1]`. Note it never excludes positions already held by other slots.
- `src/OpenSage.Mods.Generals/Gui/GameOptionsUtil.cs` (git history before the P1 refactor): the
  original code: it built `availablePositions` then did a first pass removing every
  `slot.StartPosition != 0` from the pool before the assignment pass. This is the behaviour to
  re-create.
- `src/OpenSage.Game/Network/SkirmishManager.cs`: `HostSkirmishManager.SkirmishClientUpdatePacketReceived`
  already guards start-position uniqueness for the network path; mirror that intent here.

## Steps

1. In `ApplyDefaults`, after building `availablePositions` (`1..availablePlayerCount`) and before
   the assignment loop, do a first pass over all slots that will become players (i.e. not
   `Open`/`Closed`) and `availablePositions.Remove(slot.StartPosition)` for every slot whose
   `StartPosition != 0`. This reserves manually-placed positions.
2. Keep the existing assignment loop (pop from the end of `availablePositions`) for slots with
   `StartPosition == 0`, now drawing only from genuinely-free positions.
3. Guard the empty-pool case as today (skip if no positions remain).
4. Do not change faction/color defaulting or the slot-state skip logic.

## Files likely to change

- `src/OpenSage.Game/Network/SkirmishSlotDefaults.cs`

## Acceptance criteria

- [ ] After `ApplyDefaults`, no two player slots (non-`Open`/`Closed`) share the same non-zero
      `StartPosition`.
- [ ] A slot with a pre-set `StartPosition` keeps it; only `StartPosition == 0` slots are filled,
      and only from positions not already taken.
- [ ] Standard validation passes (`dotnet build src`, `dotnet test src --filter Category!=Interactive`,
      `dotnet format src --verify-no-changes`).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not redesign start-position assignment; just restore the reserve-first pass.
- Do not change the lobby GUI flow or the network packet path.

## On completion

- Set Status to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Consider adding a unit test for `ApplyDefaults` collision-freedom (small `[Fact]` in
  `OpenSage.Game.Tests`, no game files needed).

## Result

Added a reserve-first pass in `ApplyDefaults` that removes every non-`Open`/`Closed` slot's
pre-set `StartPosition` from `availablePositions` before the original assignment loop runs, so
manually-placed positions can no longer be re-handed to another slot. Added
`src/OpenSage.Game.Tests/Network/SkirmishSlotDefaultsTests.cs` with
`ApplyDefaults_DoesNotReassignAlreadyTakenStartPositions` and
`ApplyDefaults_SkipsOpenAndClosedSlots`. Along the way found that `MockedGameTest.TestGame
.GetPlayableSides()` (test harness stub) threw `NotImplementedException`, blocking any test that
exercises `ApplyDefaults`; implemented it to mirror the real `Game.GetPlayableSides()`
(`AssetStore.PlayerTemplates.Where(x => x.PlayableSide)`). Validation: `dotnet build src` clean,
`dotnet test src --filter Category!=Interactive` 666 passed / 22 skipped / 0 failed, `dotnet format
src --verify-no-changes` reports 0 CHARSET/style errors for changed files.
