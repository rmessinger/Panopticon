# Track G · G1: High-DPI drawable & resolution correctness

> Audience: human overview above the divider; agent-context detail below it.

Implements the G1 row of [Track G](../track-g-rendering-clarity.md). Design source of truth:
[deep-dives/g1-high-dpi-resolution.md](../deep-dives/g1-high-dpi-resolution.md), including its
[architecture diagrams](../deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams).

## Scope

Separate logical points from physical pixels across the window, panel, viewport and GUI, and give
the window a native-resolution drawable. Excludes every other image-quality change in Track G, and
excludes making the GUI resolution-independent (phase G9).

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| G1-1 | [`WI-G1-01`](../agent/cards/WI-G1-01-audit-size-consumers.md) | Classify size consumers as points or pixels. |
| G1-2 | [`WI-G1-02`](../agent/cards/WI-G1-02-allow-highdpi-drawable-size.md) | `AllowHighDpi` window, pixel-size interop, `DrawableBounds`/`DpiScale`, swapchain in pixels. |
| G1-3 | [`WI-G1-03`](../agent/cards/WI-G1-03-route-render-surfaces-to-pixels.md) | Render target and 3D viewport in pixels; GUI layout on points. |
| G1-4 | [`WI-G1-04`](../agent/cards/WI-G1-04-scale-2d-and-picking.md) | Scale 2D drawing and mouse picking; completes G1. |

Cards G1-2 and G1-3 each leave the build correct at scale 1 but incomplete at scale != 1; G1-4 is
the phase's completion point. Run them in order.

---

## Agent context

Entry points and their coordinate space after the phase:

| Symbol | Space after G1 |
|---|---|
| `GameWindow.ClientBounds` | logical points (unchanged) |
| `GameWindow.DrawableBounds` | physical pixels (new) |
| `GameWindow.DpiScale` | ratio (new) |
| `GameWindow.WindowScale` / `CursorScale` | display-DPI scale, cursor sizing only (unchanged) |
| `GamePanel.Frame` / `GamePanel.ClientBounds` | physical pixels |
| `Game.Viewport` | physical pixels |
| `WndWindowManager` / `AptWindowManager` sizes | logical points |
| `InputMessage` mouse positions | logical points |

Verified constraints in the pinned dependency versions, which shape the approach:

- `WindowCreateInfo` (Veldrid.StartupUtilities 4.9.0) exposes no window-flags field, so
  `VeldridStartup.CreateWindow` cannot request `AllowHighDpi`; `Sdl2Window`'s
  `(string, int, int, int, int, SDL_WindowFlags, bool)` constructor can.
- `VeldridStartup.CreateWindow` uses `OpenGL | Resizable`, plus the state flag mapping, plus
  `Shown` when the state is not `Hidden`. Replicate exactly and add `AllowHighDpi`.
- `Sdl2Window.ScaleFactor` is hardcoded to `Vector2.One` in Veldrid.SDL2 4.9.0 and must not be used.
- Veldrid.SDL2 4.9.0 wraps no drawable-size entry point; `SDL_GetWindowSizeInPixels` (SDL 2.26+)
  must be added through the `Sdl2Interop` `Sdl2Native.LoadFunction` pattern.
- `Sdl2Window.SdlWindowHandle` supplies the raw `SDL_Window*` for that call.

Code references for each call site are in the deep dive's
[current code appendix](../deep-dives/g1-high-dpi-resolution.md#appendix-current-code-agent-context).
