# Task Card `WI-G1-02`: Create the window with `AllowHighDpi` and expose the pixel drawable size

| Field | Value |
|---|---|
| Card ID | `WI-G1-02` |
| Roadmap phase | [Track G · G1: High-DPI drawable & resolution correctness](../../track-g-rendering-clarity.md) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-G1-01` |
| Diagrams | [G1 Architecture Diagrams](../../deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams) |

## Goal

Give the window a native-resolution drawable and expose its pixel size, so the swapchain is sized
in physical pixels instead of logical points. Render-target and viewport routing stay unchanged
here; that is `WI-G1-03`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/g1-high-dpi-resolution.md` §2 and §3: the Veldrid/SDL constraints that
  dictate the approach, and the `ClientBounds`/`DrawableBounds`/`DpiScale` split.
- `src/OpenSage.Game/GameWindow.cs:36-43,73-129`: `ClientBounds`, `WindowScale`, `CursorScale`, the
  constructor, `CreateGraphicsDevice`, `HandleResized`.
- `src/OpenSage.Game/Utilities/Sdl2Interop.cs:1-80`: the `Sdl2Native.LoadFunction` delegate pattern
  to copy, and `GetDisplayScale`.

## Steps

1. In `Sdl2Interop`, add an `SDL_GetWindowSizeInPixels` P/Invoke following the existing pattern
   exactly (private delegate, `Sdl2Native.LoadFunction` static field, public forwarding method).
   Signature: `void SDL_GetWindowSizeInPixels(SDL_Window window, int* w, int* h)`.
2. Add a helper `Size GetWindowSizeInPixels(SDL_Window window)` that calls it and returns the
   result. If SDL returns a zero width or height (entry point missing on an older SDL), fall back to
   the caller-supplied logical size rather than throwing.
3. In `GameWindow`'s constructor, replace the `VeldridStartup.CreateWindow` call with a direct
   `new Sdl2Window(title, x, y, width, height, flags, threadedProcessing: false)`. Replicate the
   flags `VeldridStartup.CreateWindow` uses and add `AllowHighDpi`:
   `SDL_WindowFlags.OpenGL | SDL_WindowFlags.Resizable | <state flags> | SDL_WindowFlags.AllowHighDpi`,
   plus `SDL_WindowFlags.Shown` when the initial state is not `Hidden`. Map `WindowState` to flags
   as: `Normal` → none, `FullScreen` → `Fullscreen`, `Maximized` → `Maximized`,
   `Minimized` → `Minimized`, `BorderlessFullScreen` → `FullScreenDesktop`, `Hidden` → `Hidden`.
4. Remove the `WindowScale *` pre-multiplication of `x`, `y`, `width` and `height` in the
   constructor. With `AllowHighDpi` the requested size is already in logical points and SDL scales
   the drawable, so pre-multiplying would now double-apply the scale.
5. Add `public Rectangle DrawableBounds` returning `new Rectangle(0, 0, w, h)` from the pixel size
   of `_window.SdlWindowHandle`, and `public float DpiScale` returning
   `DrawableBounds.Width / (float)ClientBounds.Width` (guarding against a zero width by returning
   `1.0f`). Leave `ClientBounds` returning `_window.Bounds` unchanged.
6. Keep `WindowScale` as-is for `CursorScale` only. Do not repoint `CursorScale` at `DpiScale`.
7. In `HandleResized`, size the swapchain from `DrawableBounds` instead of `_window.Bounds`.
8. Add a unit test asserting that `DpiScale` returns `1.0f` when the drawable and logical widths
   match, and that a zero logical width yields `1.0f` rather than a divide-by-zero. Extract the
   ratio calculation into an internal static helper if that is needed to test it without a window.

## Files likely to change

- `src/OpenSage.Game/Utilities/Sdl2Interop.cs`
- `src/OpenSage.Game/GameWindow.cs`
- `src/OpenSage.Game.Tests/`: new unit test.

## Acceptance criteria

- [ ] The window is created with `AllowHighDpi` set, via `new Sdl2Window(...)` rather than
      `VeldridStartup.CreateWindow`.
- [ ] No `WindowScale` multiplication remains in the `GameWindow` constructor.
- [ ] `GameWindow.DrawableBounds` and `GameWindow.DpiScale` exist; `ClientBounds` still returns
      logical points.
- [ ] `Swapchain.Resize` in `HandleResized` is fed `DrawableBounds`.
- [ ] The `DpiScale` helper test passes, including the zero-width guard.
- [ ] The game still launches windowed at scale 1 with no visible change.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change `GamePanel`, `RenderTarget`, `Game.OnPanelSizeChanged`, or any
  `Panel.EnsureFrame` call site. The frame will still be sized in points after this card, and that
  is expected: `WI-G1-03` moves it.
- Do not touch the 2D drawing path or mouse input mapping (`WI-G1-04`).
- Do not repoint `CursorScale` or delete `WindowScale`.
- If replicating the Veldrid window flags turns out to change windowed/fullscreen behaviour at
  scale 1, mark `Blocked` and report rather than adjusting unrelated window handling.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - None; `WI-G1-03` is next and already exists.
