# Task Card `WI-G1-04`: Convert mouse input to pixels at the window boundary

| Field | Value |
|---|---|
| Card ID | `WI-G1-04` |
| Roadmap phase | [Track G · G1: High-DPI drawable & resolution correctness](../../track-g-rendering-clarity.md) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-G1-01`, `WI-G1-02`, `WI-G1-03` |
| Diagrams | [G1 Architecture Diagrams](../../deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams) |

## Goal

Close the correctness gap `WI-G1-03` leaves open at scale != 1: mouse input arrives in points while
the 3D viewport and GUI are in pixels. The `WI-G1-01` audit found about fifteen mouse consumers and
no single picking boundary, and found WND layout already proportional, so the conversion happens
once where SDL mouse events become `InputMessage`s and no 2D transform is needed. Completes
phase G1.

## Context to read (and only this)

- `docs/roadmap/deep-dives/g1-high-dpi-resolution.md` §3 (the two consequences) and §4 (validation).
- The `## Consumer classification` table in `docs/roadmap/implementation/track-g-g1-high-dpi.md`.
- `src/Panopticon.Simulation/GameWindow.cs`: `HandleMouseMove`, `HandleMouseDown`, `HandleMouseUp`.
- `src/Panopticon.Simulation/Gui/Wnd/Controls/Window.cs:61-74`: the proportional root transform.

## Steps

1. In `GameWindow.HandleMouseMove`, convert the SDL position from points to pixels with
   `DpiScale` before storing `_lastMouseX`/`_lastMouseY` and creating the message. Button messages
   reuse the stored position.
2. Add `DpiScaling.ToPixels` overloads for `Point2D` and `Rectangle`.
3. Add unit tests: the conversion is the identity at scale 1 and multiplies at scale 2; a centre
   mouse position keeps its normalised position; and through a WND root transform fitted to the
   pixel size it maps to the same client point at scale 1 and 2.

## Files likely to change

- `src/Panopticon.Simulation/GameWindow.cs`
- `src/Panopticon.Simulation/DpiScaling.cs`
- `test/Panopticon.Simulation.Tests/DpiScalingTests.cs`

## Acceptance criteria

- [x] A centre mouse position keeps its normalised position at `DpiScale` 1, 1.5 and 2 (unit test).
- [x] A converted mouse position maps to the same WND client point at `DpiScale` 1 and 2, so GUI
      elements keep the same fraction of the frame (unit test on the transform).
- [x] `Camera` contains no `DpiScale` reference; the conversion lives at the input boundary.
- [x] TestHarness at scale 1 is visually unchanged from before Track G (headless capture).
- [x] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes

# Visual check (requires a game install; see the TestHarness AGENTS.md workflow)
dotnet run --project src/Panopticon.Tools.TestHarness
```

## Out of scope / guardrails

- Do not make the GUI genuinely resolution-independent (re-laying out in a virtual coordinate
  system, higher-resolution art, DPI-aware font rasterisation). That is phase
  [G9](../../track-g-rendering-clarity.md). This card only preserves the current physical size at
  native resolution.
- Do not add antialiasing, change samplers, or touch the alpha-test path. Those are G4/G5/G7.
- Do not scale inside individual mouse consumers; the only conversion is in `GameWindow`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Set the G1 phase Status to `Done` in
  [`track-g-rendering-clarity.md`](../../track-g-rendering-clarity.md) and tick the G1 item in the
  [implementation checklist](../../implementation-checklist.md), if all four G1 cards are `Done`.
- Update the Track G [Progress](../../README.md#progress) table.
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - A card decomposing G2 (graphics settings & fidelity presets), which every later G phase depends on.
