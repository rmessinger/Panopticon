# Task Card `WI-P8-03`: End-of-match UI and TestHarness integration

| Field | Value |
|---|---|
| Card ID | `WI-P8-03` |
| Roadmap phase | [Track P · P8](../../track-p-playable-baseline.md): sub-phase P8-3, see [deep dive](../../deep-dives/p8-implementation-delta-remediation.md#3-sub-phases); remediates a gap in [P3 victory/defeat](../../deep-dives/p3-victory-defeat.md#14-end-of-match-flow) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-P3-04` |
| Diagrams | [P8 Architecture Diagrams](../../deep-dives/p8-implementation-delta-remediation.md#6-architecture-diagrams) |

## Goal

`WI-P3-04` deliberately scoped out interactive UI/exit consumption of `GameOutcome`, leaving only
the headless `IsLogicRunning = false` transition. Two consequences remain open: (1) there is no
interactive win/lose surfacing at all, and (2) `OpenSage.Tools.TestHarness` still uses its own ad
hoc "exit after enemy Command Center destroyed" path instead of `GameOutcome`, which is exactly the
mechanism §1.1 of the deep dive says `GameOutcome` should replace. Close both: add a minimal
win/lose UI hook usable by `Mods.Generals`, and switch `TestHarness` to terminate on
`GameOutcome.Status == Decided` instead of its own enemy-CC-destroyed check.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p3-victory-defeat.md:10-44`: `GameOutcome` purpose, explicitly naming the `TestHarness/Program.cs:232-267` ad hoc exit path as what it replaces.
- `docs/roadmap/deep-dives/p3-victory-defeat.md:78-89`: end-of-match flow design — UI dialog for interactive play, structured outcome for headless.
- `docs/roadmap/agent/cards/WI-P3-04-end-of-match-flow.md`: prior card; note its "Out of scope / guardrails" explicitly deferred UI work with "expose a minimal event/state hook and document a follow-up" — this card is that follow-up.
- `src/OpenSage.Game/Logic/VictorySystem.cs`: `CurrentOutcome`, `TrySetDecidedOutcome` — the transition point to hook a notification from.
- `src/OpenSage.Game/Game.cs:95,462`: `Game.VictorySystem`/`Game.CurrentOutcome` public surface already available to consumers.
- `src/OpenSage.Tools.TestHarness/Program.cs:270-315`: current ad hoc termination — the enemy-base-destroyed check and runout-tail logic to replace with a `GameOutcome`-driven check.
- `src/OpenSage.Mods.Generals/Gui/GeneralsControlBar.cs` and sibling GUI files: existing pattern for a `Mods.Generals`-level UI consumer, to follow for a win/lose dialog hook (a minimal event/state surface is acceptable; a full dialog is not required if time-boxed — document what was actually built).

## Steps

1. Add a lightweight, engine-level notification for `Status → Decided` (e.g. an event on `VictorySystem`/`Game`, or expose `CurrentOutcome` polling — pick whichever the existing `GameSystem`/event conventions in this codebase favor; check `GameLogic.ObjectLifecycleChanged` for the established pattern).
2. In `Mods.Generals`, add a minimal consumer: at minimum a documented hook point where a win/lose UI could subscribe (e.g. logging the outcome, or a stub panel), explicitly scoped to "minimal" per the prior card's guardrail — do not build new full-screen UI art/layout from scratch if it requires new asset/GUI-scripting infrastructure beyond what already exists; if a full dialog is genuinely out of reach in this card's effort budget, implement the hook and log a clear follow-up instead of skipping silently.
3. In `OpenSage.Tools.TestHarness/Program.cs`, replace the enemy-CC-destroyed detection (`ActionType.EnemyBaseDestroyed` check around `:302`) with a check on `game.CurrentOutcome.Status == OutcomeStatus.Decided`, preserving the existing "run a short tail period then exit" behavior.
4. Verify `TestHarness` still exits cleanly for a scenario that reaches a decided outcome, and does not exit early for an in-progress match.
5. Add or extend a headless `[Fact]` test asserting the new notification/hook fires exactly once when `VictorySystem` transitions to `Decided`.
6. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/VictorySystem.cs`
- `src/OpenSage.Game/Game.cs`
- `src/OpenSage.Mods.Generals/Gui/` (new or extended consumer)
- `src/OpenSage.Tools.TestHarness/Program.cs`
- `src/OpenSage.Game.Tests/Logic/Victory/` (new or extended test file)

## Acceptance criteria

- [ ] A `Status → Decided` transition raises a documented notification consumable outside `VictorySystem` (event or equivalent), covered by a headless test asserting it fires exactly once.
- [ ] `Mods.Generals` has a minimal, documented win/lose consumer hook (full dialog UI is a bonus, not required, but must not be silently dropped — if not built, the Result section states exactly what was built instead and why).
- [ ] `OpenSage.Tools.TestHarness/Program.cs` terminates based on `GameOutcome.Status == Decided` instead of the old enemy-CC-destroyed check, preserving the existing tail-then-exit behavior.
- [ ] `TestHarness` does not exit early for an in-progress (non-decided) match.
- [ ] Headless `[Fact]` tests are not skipped and do not require game files.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Victory"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not build new GUI asset/layout infrastructure; reuse existing `Mods.Generals` GUI patterns.
- Do not remove the "tail period" behavior in `TestHarness`; only change the trigger condition.
- Do not change `GameOutcome`/`VictorySystem` semantics established by `WI-P3-01` through `WI-P3-07` and `WI-P8-02`; this card only adds a notification/consumer on top.
- If a full win/lose dialog is genuinely out of scope for this card's effort budget, that must be explicit in the Result section with a named follow-up, not silently absent.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the P8-3 row in [`deep-dives/p8-implementation-delta-remediation.md`](../../deep-dives/p8-implementation-delta-remediation.md#3-sub-phases).
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - Full win/lose dialog UI, if only a minimal hook was built here.

## Result

<!-- append after completing -->
