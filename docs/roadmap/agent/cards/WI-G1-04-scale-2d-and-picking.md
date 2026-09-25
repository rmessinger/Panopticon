# Task Card `WI-G1-04`: Scale 2D drawing and mouse picking by `DpiScale`

| Field | Value |
|---|---|
| Card ID | `WI-G1-04` |
| Roadmap phase | [Track G · G1: High-DPI drawable & resolution correctness](../../track-g-rendering-clarity.md) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-G1-01`, `WI-G1-02`, `WI-G1-03` |
| Diagrams | [G1 Architecture Diagrams](../../deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams) |

## Goal

Close the two correctness gaps `WI-G1-03` leaves open at scale != 1: the GUI drawing at `1/scale`
size in the corner of the pixel framebuffer, and mouse picking drifting because input arrives in
points while the 3D viewport is now in pixels. Completes phase G1.

## Context to read (and only this)

- `docs/roadmap/deep-dives/g1-high-dpi-resolution.md` §3 (the two consequences) and §4 (validation).
- The `## Consumer classification` table in `docs/roadmap/implementation/track-g-g1-high-dpi.md`.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs:130-160`: the `DrawingContext2D.Begin`
  call and the 2D block.
- `src/Panopticon.Simulation/Graphics/Cameras/Camera.cs:140-200`: `Unproject`, `Project`, `ScreenSize`.
- `src/Panopticon.Simulation/Game.cs:666-683`: `OnPanelSizeChanged` as left by `WI-G1-03`.

## Steps

1. Find the picking boundary: the call path from an `InputMessage` mouse position to
   `Camera.Unproject`. Scale the mouse position by `DpiScale` at that single boundary so a point
   position becomes a pixel position before unprojection. Do not scale inside `Camera` itself, which
   must stay a pure pixel-space type.
2. If a symmetrical conversion exists (a world position projected back to a screen position for
   GUI placement, via `Camera.Project`), divide by `DpiScale` there so it returns points.
3. Apply a `DpiScale` transform to the 2D layer so GUI laid out in points fills the pixel
   framebuffer at its original physical size. Prefer scaling in `DrawingContext2D` over changing the
   size passed to `Begin`, so 2D content is drawn at native resolution rather than upscaled from a
   smaller surface.
4. Add unit tests: a mouse position at the centre of the window unprojects to the same world ray at
   `DpiScale` 1 and 2; and a GUI element at logical position `(x, y)` maps to pixel position
   `(x * scale, y * scale)`.
5. Manually verify with the TestHarness that the HUD is the same physical size as before and that
   clicking a unit selects it, at both scale 1 and a forced scale of 2.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`
- `src/Panopticon.Simulation/Gui/DrawingContext2D.cs`
- The input-to-picking call site identified in step 1.
- `test/Panopticon.Simulation.Tests/`: new unit tests.

## Acceptance criteria

- [ ] Mouse picking resolves to the same world position at `DpiScale` 1 and 2 (unit test).
- [ ] GUI drawn through the 2D layer occupies the same fraction of the frame at `DpiScale` 1 and 2
      (unit test on the transform).
- [ ] `Camera` contains no `DpiScale` reference; the conversion lives at the input boundary.
- [ ] TestHarness at scale 1 is visually unchanged from before Track G.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

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
- If the picking boundary turns out to be spread across more than two call sites, mark `Blocked`
  and report rather than scaling in each one.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Set the G1 phase Status to `Done` in
  [`track-g-rendering-clarity.md`](../../track-g-rendering-clarity.md) and tick the G1 item in the
  [implementation checklist](../../implementation-checklist.md), if all four G1 cards are `Done`.
- Update the Track G [Progress](../../README.md#progress) table.
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - A card decomposing G2 (graphics settings & fidelity presets), which every later G phase depends on.
