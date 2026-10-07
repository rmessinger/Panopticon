# Task Card `WI-G1-03`: Route render surfaces and GUI layout to pixels

| Field | Value |
|---|---|
| Card ID | `WI-G1-03` |
| Roadmap phase | [Track G · G1: High-DPI drawable & resolution correctness](../../track-g-rendering-clarity.md) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-G1-01`, `WI-G1-02` |
| Diagrams | [G1 Architecture Diagrams](../../deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams) |

## Goal

Size the `GamePanel` render target, the 3D `Viewport` and the WND/APT GUI layout in physical
pixels, and give ImGui the window's framebuffer scale. The `WI-G1-01` audit replaced the original
points/pixels split for the GUI: WND layout is already proportional to the size it is given, so one
pixel size serves both (see the
[design adjustment](../../implementation/track-g-g1-high-dpi.md)).

## Context to read (and only this)

- The `## Consumer classification` table in
  `docs/roadmap/implementation/track-g-g1-high-dpi.md` (produced by `WI-G1-01`): the authoritative
  list of which consumer needs which space.
- `docs/roadmap/deep-dives/g1-high-dpi-resolution.md` §3: the design and its two consequences.
- `src/Panopticon.Simulation/GamePanel.cs`: `Frame`, `ClientBounds`, `EnsureFrame`.
- `src/Panopticon.Simulation/Game.cs:630-645,666-683`: the `EnsureFrame` call and `OnPanelSizeChanged`.
- `src/Panopticon.Launcher/Program.cs:340-350`, `src/Panopticon.Tools.TestHarness/ReplayScenarioRunner.cs`,
  `ValidationGroundRunner.cs` and `MassBattleDeploymentRunner.cs`: the other `EnsureFrame` call sites.
- `src/Veldrid.ImGui/ImGuiRenderer.cs`, `src/Panopticon.Simulation/Diagnostics/DeveloperModeView.cs`,
  `GameView.cs` and `src/Panopticon.Tools.TestHarness/ValidationGroundOverlay.cs`: ImGui sizing.

## Steps

1. Change every window-backed `Panel.EnsureFrame(window.ClientBounds)` call site to pass
   `window.DrawableBounds`: `Game.cs`, `Panopticon.Launcher/Program.cs`, and the three TestHarness
   runners. Keep the existing null-window fallback in `Game.cs` unchanged.
2. Add an XML doc comment on `GamePanel.Frame` and `GamePanel.ClientBounds` stating that both are
   in physical pixels. Do not rename them.
3. Leave `Game.OnPanelSizeChanged` feeding `Panel.ClientBounds.Size` (now pixels) to both the
   `Viewport` and the GUI managers.
4. Add `ImGuiRenderer.WindowResized(int width, int height, float scale)` and feed it
   `DrawableBounds` plus `DpiScale` from `DeveloperModeView` and `ValidationGroundOverlay`, so ImGui
   lays out in points and scales its clip rects to the pixel framebuffer.
5. In `GameView`, convert the ImGui-space panel rectangle to pixels with `DpiScale` before
   `EnsureFrame`, so it matches the pixel-space input messages.

## Files likely to change

- `src/Panopticon.Simulation/Game.cs`
- `src/Panopticon.Simulation/GamePanel.cs`
- `src/Panopticon.Launcher/Program.cs`
- `src/Panopticon.Tools.TestHarness/*Runner.cs`, `ValidationGroundOverlay.cs`
- `src/Panopticon.Simulation/Diagnostics/DeveloperModeView.cs`, `GameView.cs`
- `src/Veldrid.ImGui/ImGuiRenderer.cs`

## Acceptance criteria

- [x] Every window-backed `EnsureFrame` call site passes `DrawableBounds`.
- [x] `Game.Viewport` and the WND/APT managers receive the pixel size.
- [x] ImGui renders with `DpiScale` as its framebuffer scale.
- [x] The game still launches and renders correctly at scale 1 with no visible change. Verified by
      the headless capture path; windowed runs are unavailable on the validation host.
- [x] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not convert mouse input here. After this card, on a display with scale != 1, picking and GUI
  hit testing are offset until `WI-G1-04` lands.
- Do not change WND/APT layout logic itself; only the size value handed to it.
- Do not rename `GamePanel.ClientBounds` or `Frame`, which would ripple beyond this card.
- If the classification table from `WI-G1-01` lists a consumer not covered by these steps, mark
  `Blocked` and report rather than improvising.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - None; `WI-G1-04` is next and already exists.
