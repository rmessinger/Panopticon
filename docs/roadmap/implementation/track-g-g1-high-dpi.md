# Track G · G1: High-DPI drawable & resolution correctness

> Audience: human overview above the divider; agent-context detail below it.

Implements the G1 row of [Track G](../track-g-rendering-clarity.md). Design source of truth:
[deep-dives/g1-high-dpi-resolution.md](../deep-dives/g1-high-dpi-resolution.md), including its
[architecture diagrams](../deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams).

## Scope

Create the window with a native-resolution drawable, size every render surface in physical
pixels, and keep input, GUI hit-testing and picking consistent with those surfaces. Points remain
only at the OS boundary: window creation size, SDL mouse events, the OS cursor, and ImGui layout.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-G1-01`](../agent/cards/WI-G1-01-audit-size-consumers.md) | Consumer classification below. |
| 2 | [`WI-G1-02`](../agent/cards/WI-G1-02-allow-highdpi-drawable-size.md) | `AllowHighDpi` window, `DrawableBounds`, `DpiScale`, swapchain in pixels. |
| 3 | [`WI-G1-03`](../agent/cards/WI-G1-03-route-render-surfaces-to-pixels.md) | Panel, viewport and GUI layout in pixels. |
| 4 | [`WI-G1-04`](../agent/cards/WI-G1-04-scale-2d-and-picking.md) | Mouse input and ImGui converted at the window boundary. |

## Design adjustment from the audit

The deep dive planned GUI layout in points with a `DpiScale` transform in `DrawingContext2D`,
and a mouse conversion at a single picking boundary. The audit found neither holds:

- WND and APT layout are already resolution-proportional. `Window.OnSizeChanged` fits the WND
  creation resolution (800x600) to whatever size it is given, and `DrawingContext2D` rasterises
  text at the transformed size. Handing the GUI pixels keeps the HUD at the same fraction of the
  frame and draws it at native resolution, with no extra transform.
- Mouse positions are consumed in about fifteen places that compare them with pixel-space values:
  camera picking, box selection (`SelectionSystem.GetSelectionFrustum`), edge scroll and drag
  anchors (`TacticalView`, `LookAtTranslator`), WND/APT hit-testing, radial buttons and the debug
  overlay. There is no single picking boundary.

G1 therefore uses one engine-wide space, physical pixels, and converts mouse positions once where
they enter: `GameWindow`'s SDL event handlers. `WI-G1-03` and `WI-G1-04` follow this design; the
deep dive §3 records it.

---

## Agent context

### Consumer classification

Produced by `WI-G1-01` against `main` at `d2902f192`. Classification is by ultimate use. `both`
marks a single value feeding a points consumer and a pixels consumer.

| Site | Subsystem | Value | Class | Notes |
|---|---|---|---|---|
| `Panopticon.Simulation/Game.cs:646` | Panel setup | `window.ClientBounds` to `EnsureFrame` | pixels | Render-target allocation. |
| `Panopticon.Launcher/Program.cs:345` | Launcher loop | `window.ClientBounds` to `EnsureFrame` | pixels | |
| `Panopticon.Tools.TestHarness/ReplayScenarioRunner.cs:82` | TestHarness loop | `window.ClientBounds` to `EnsureFrame` | pixels | |
| `Panopticon.Tools.TestHarness/ValidationGroundRunner.cs:398` | TestHarness loop | `window.ClientBounds` to `EnsureFrame` | pixels | |
| `Panopticon.Tools.TestHarness/MassBattleDeploymentRunner.cs:688` | TestHarness loop | `window.ClientBounds` to `EnsureFrame` | pixels | |
| `Panopticon.Simulation/Game.cs:676-692` | `OnPanelSizeChanged` | `Panel.ClientBounds.Size` | both | 3D `Viewport` (pixels) and WND/APT `OnViewportSizeChanged` (layout). |
| `Panopticon.Simulation/Gui/Wnd/WndWindowManager.cs:36` | WND layout | `Panel.ClientBounds.Size` | pixels | Proportional layout; see design adjustment. |
| `Panopticon.Simulation/Gui/Apt/AptWindowManager.cs:30` | APT layout | `Panel.ClientBounds.Size` | pixels | Proportional layout. |
| `Panopticon.Simulation/Scene3D.cs:137` | Camera viewport | `game.Viewport` | pixels | |
| `Panopticon.Simulation/Logic/SelectionSystem.cs:268` | Box selection | `Game.Viewport` | pixels | Compared with mouse rectangles. |
| `Panopticon.Simulation/Graphics/Cameras/TacticalView.cs:1436,1450,1774` | Camera centre, edge scroll | `Camera.ScreenSize` | pixels | Compared with mouse positions. |
| `Panopticon.Simulation/Graphics/Cameras/LookAtTranslator.cs:307,447-450,490` | Drag and rotate anchors | `Camera.ScreenSize` | pixels | Compared with mouse positions. |
| `Panopticon.Simulation/Input/Cursors/CursorManager.cs:70` | OS cursor | `window.CursorScale` | points | SDL cursors are OS-scaled; stays on `WindowScale`. |
| `Panopticon.Simulation/Diagnostics/DeveloperModeView.cs:30-35` | ImGui | `window.ClientBounds` | both | ImGui display size (points) and swapchain target (pixels). |
| `Panopticon.Tools.TestHarness/ValidationGroundOverlay.cs:78-83` | ImGui | `window.ClientBounds` | both | Same as `DeveloperModeView`. |
| `Panopticon.Simulation/Diagnostics/MainView.cs:333` | ImGui layout | `window.ClientBounds.Width` | points | ImGui coordinates. |
| `Panopticon.Simulation/Diagnostics/GameView.cs:33-41` | Developer-mode game view | ImGui rect to `EnsureFrame`, `Panel.Frame` to `TranslateInputMessages` | both | ImGui rect is points; panel and input translation must be pixels. |
| `Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs:1177`, `ValidationGroundRunner.cs:341`, `MassBattleDeploymentRunner.cs:663`, `ReplayScenarioRunner.cs:179` | Offscreen capture | Fixed `Rectangle` | pixels | No window; unaffected. |
| `Panopticon.Tools.WorldBuilder.Core/GameViewportRenderer.cs:86,111,138` | WorldBuilder | Host-supplied size | pixels | Avalonia host owns its scaling; unaffected. |
| `GameWindow.HandleMouseMove`/`HandleMouseDown`/`HandleMouseUp` | Input source | SDL mouse position | points | Every `InputMessage.MousePosition` consumer compares with pixel values; convert here. |

Ambiguous entries: 0.

### Platform notes

- `Sdl2Native.LoadFunction` returns null for a missing export rather than throwing.
- The Windows `SDL2.dll` bundled with Veldrid.SDL2 4.9 lacks `SDL_GetWindowSizeInPixels`;
  `Sdl2Interop` falls back to `SDL_GL_GetDrawableSize`, then to the logical size. That SDL is not
  DPI-aware, so Windows stays at scale 1 until the native SDL is upgraded.
- X11 never scales the drawable. Where `DpiScale` is 1 but `WindowScale` is not, `GameWindow`
  keeps the pre-G1 behaviour of enlarging the window by `WindowScale`.
- Xvfb runs at scale 1, so scale-2 behaviour is covered by unit tests on the conversion helpers
  rather than by a windowed run on this host.
