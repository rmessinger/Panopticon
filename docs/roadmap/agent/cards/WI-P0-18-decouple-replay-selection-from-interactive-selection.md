# Task Card: `WI-P0-18`: Decouple replay selection from interactive UI selection

| Field | Value |
|---|---|
| Card ID | `WI-P0-18` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/p0-playable-baseline-harness.md#8-architecture-diagrams) |

## Goal

Stop a replay's own recorded `SetSelection`/`ClearSelection` orders from overwriting a human
observer's interactive drag-selection in `OpenSage.Tools.TestHarness`. Today both paths funnel
through the single shared `SelectionSystem`/`Game.Selection`, so when the replay issues a
selection order for the same player the human is inspecting, the human's box-select is silently
stomped: no rectangle survives, and the wrong units end up selected. Reported symptom: "selection
is only broken when the replayer has a game object selected."

## Context to read (and only this)

- `src/OpenSage.Game/Logic/SelectionSystem.cs`: `SetSelectedObjects` (called by both the replay's
  `OrderProcessor` and the interactive `SingleSelect`/`MultiSelect` paths), and
  `OnStartDragSelection`/`OnDragSelection`/`OnEndDragSelection` (interactive drag state machine).
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:280-300`: `OrderType.SetSelection` and
  `OrderType.ClearSelection` handling; both call `_game.Selection.SetSelectedObjects(...)`.
- `src/OpenSage.Game/Logic/SelectionMessageHandler.cs`: routes real mouse input into
  `SelectionSystem`.
- `src/OpenSage.Game/Network/NetworkMessageBuffer.cs:26`: `IsReplay`, already used by
  `SelectionSystem.MultiSelect` for the "box-select any object while watching a replay" fix; the
  same signal is the natural gate for this card.
- `src/OpenSage.Tools.TestHarness/AGENTS.md`: TestHarness is a windowed replay viewer; this is the
  primary place a human interacts with a running replay.

## Steps

1. Confirm the conflict: during `IsReplay` playback, a recorded `SetSelection`/`ClearSelection`
   order for a given player calls `SelectionSystem.SetSelectedObjects` on that player, which (when
   `player == Game.Scene3D.LocalPlayer`) also reassigns `Game.OrderGenerator.ActiveGenerator`. Add a
   headless test that starts a drag (`OnStartDragSelection`/`OnDragSelection`), then, mid-drag,
   injects a `SetSelection` order for the same player via `OrderProcessor`, and shows the human's
   `SelectionSystem.Status`/`SelectionGui.SelectionBoxVisible` gets reset or overwritten.
2. Design the decoupling. Two reasonable shapes (pick the smaller one that satisfies the AC):
   - Track the replay's "recorded selection" as separate state (e.g. a per-player set exposed for
     UI display, such as highlighting) from the human observer's own `SelectionSystem` state, only
     merging them when not `IsReplay`.
   - Or: while `IsReplay` is true and a human drag (`SelectionSystem.Selecting`) is in progress,
     have `OrderProcessor`'s `SetSelection`/`ClearSelection` handling skip applying to the
     interactive `SelectionSystem` for that frame (still apply gameplay-visible effects, if any,
     elsewhere) and resume once the drag ends.
   - Do not change live (non-replay) behavior: a real game's own selection orders (from network
     play) must keep working exactly as today.
3. Implement the chosen approach with the smallest surface area; keep `SelectionSystem` the single
   owner of drag-selection UI state.
4. Add a regression test covering: human starts a drag, a replay-originated `SetSelection` order
   for the same player is applied mid-drag, the human's drag completes and selects the
   human-dragged units (not the replay's).

## Files likely to change

- `src/OpenSage.Game/Logic/SelectionSystem.cs`
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs`
- `src/OpenSage.Game.Tests/...` (new test, colocate with existing selection/order-processor tests)

## Acceptance criteria

- [ ] A headless test reproduces the conflict (fails against current code) then passes against the
      fix: a human drag survives a same-frame replay-issued `SetSelection`/`ClearSelection` order
      for the same player.
- [ ] Live (non-replay) selection-order handling is unchanged; an existing or new test confirms a
      network-originated `SetSelection` order still applies normally when no human drag is active.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change `MultiSelect`'s existing `IsReplay` owner-check relaxation (box-selecting any
  object's stats while watching a replay); this card is about state ownership/timing, not who can
  be selected.
- Do not add a general pub/sub or event-bus refactor of `SelectionSystem`; keep the fix localized.
- Do not change `OpenSage.Tools.ReplaySketch` scenario authoring/export; this is purely a runtime
  playback/UI concern.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the `implementation-checklist.md` P0 item this card closes.
- Append a result line to repo-root `.exchange`.
