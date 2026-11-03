# Task Card `WI-G13-01`: View mode seam and relative mouse

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G13-01` |
| Roadmap phase | [Track G · G13](../../track-g-rendering-clarity.md): First-person ground view |
| Status | `Ready` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | G2, G3 (Done) |
| Diagrams | [G13 architecture](../../deep-dives/g13-first-person-ground-view.md#5-architecture-diagrams) |

## Goal

Add the view-mode seam that lets a second camera controller replace `TacticalView` and restore it exactly, plus relative mouse input and the mode hotkey.

## Context to read (and only this)

- [G13 design](../../deep-dives/g13-first-person-ground-view.md) sections 1, 2, 3.1, and both diagrams.
- `src/Panopticon.Simulation/TacticalView.cs`, `Graphics/Cameras/Camera.cs`, `Scene3D.cs`, `LookAtTranslator.cs`.
- `src/Panopticon.Simulation/Input/HandlingPriority.cs`, `Input/InputMessageBuffer.cs`, `GameWindow.cs`, `Utilities/Sdl2Interop.cs`, `Game.cs:566-589`, `Gui/DebugUI/DebugMessageHandler.cs`.

## Steps

1. Add `ViewMode`, `ViewModeController` on `Scene3D`, `TacticalViewSnapshot` (target, yaw, pitch, zoom, height above ground, lock and snap state) with `Capture` and `Restore`, and `IViewCameraController`. In `Tactical` nothing changes.
2. Add `GameWindow.SetRelativeMouseMode(bool)` through `Sdl2Interop` (`SDL_SetRelativeMouseMode`) and `InputMessageType.MouseRelativeMove` carrying `xrel`/`yrel`; release on focus loss and on window close.
3. Add `ViewModeHotkey` (default `F7`). List every hard-coded key handler and the `CommandMap.ini` bindings in Generals and Zero Hour, and pick another default if `F7` collides; record the result in the plan.
4. Add a stub `FirstPersonCameraController` that holds a fixed eye pose, and a `FirstPersonInputHandler` that, in `FirstPerson`, consumes all keyboard and mouse messages before selection and order generators. `Escape` or the hotkey returns to `Tactical`.
5. Tests: snapshot round-trip leaves `TacticalView` state bit-identical; in `FirstPerson`, `LookAtTranslator` and selection receive no messages; relative mode is released on exit and focus loss.

## Files likely to change

- `src/Panopticon.Simulation/ViewModes/` (new: `ViewMode.cs`, `ViewModeController.cs`, `TacticalViewSnapshot.cs`, `IViewCameraController.cs`, `FirstPersonCameraController.cs`, `FirstPersonInputHandler.cs`)
- `src/Panopticon.Simulation/Scene3D.cs`, `TacticalView.cs` (snapshot accessors), `GameWindow.cs`, `Input/InputMessage*.cs`, `Utilities/Sdl2Interop.cs`
- `test/Panopticon.Simulation.Tests/ViewModes/` (new)

## Acceptance criteria

- [ ] Entering and leaving the mode restores `TacticalView` state exactly (test).
- [ ] RTS input is fully suppressed in `FirstPerson` and works unchanged after exit.
- [ ] The hotkey has no collision with existing handlers or `CommandMap.ini` (recorded).
- [ ] Existing G3 goldens unchanged.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Drop marker, locomotion, and rendering changes (`WI-G13-02` to `WI-G13-05`).
- Wiring `CommandMap.ini` dispatch.
- Presentation only: no `GameObject`, order, or logic random. A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-G13-02` and `WI-G13-03` to `Ready`.
- Tick G13-1 in the checklist and update the README progress row.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
