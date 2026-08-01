# Task Card `WI-G1-03`: Route render surfaces to pixels, keep GUI layout on points

| Field | Value |
|---|---|
| Card ID | `WI-G1-03` |
| Roadmap phase | [Track G · G1: High-DPI drawable & resolution correctness](../../track-g-rendering-clarity.md) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-G1-01`, `WI-G1-02` |
| Diagrams | [G1 Architecture Diagrams](../../deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams) |

## Goal

Size the `GamePanel` render target and the 3D `Viewport` in physical pixels while the WND/APT GUI
layout keeps receiving logical points, splitting the single size value that
`Game.OnPanelSizeChanged` currently feeds to both.

## Context to read (and only this)

- The `## Consumer classification` table in
  `docs/roadmap/implementation/track-g-g1-high-dpi.md` (produced by `WI-G1-01`): the authoritative
  list of which consumer needs which space.
- `docs/roadmap/deep-dives/g1-high-dpi-resolution.md` §3: the design and its two consequences.
- `src/OpenSage.Game/GamePanel.cs`: `Frame`, `ClientBounds`, `EnsureFrame`.
- `src/OpenSage.Game/Game.cs:630-645,666-683`: the `EnsureFrame` call and `OnPanelSizeChanged`.
- `src/OpenSage.Launcher/Program.cs:340-350` and
  `src/OpenSage.Tools.TestHarness/Program.cs:395-410,450-460`: the other `EnsureFrame` call sites.

## Steps

1. Change every `Panel.EnsureFrame(window.ClientBounds)` call site to pass
   `window.DrawableBounds`. There are three: `Game.cs`, `OpenSage.Launcher/Program.cs`, and
   `OpenSage.Tools.TestHarness/Program.cs` (two calls). Keep the existing null-window fallback in
   `Game.cs` unchanged.
2. Add an XML doc comment on `GamePanel.Frame` and `GamePanel.ClientBounds` stating that both are
   now in physical pixels. Do not rename them.
3. In `Game.OnPanelSizeChanged`, keep `Viewport` built from `Panel.ClientBounds.Size` (now pixels),
   and feed the GUI managers a logical size instead: divide the pixel size by the window's
   `DpiScale`, rounding to the nearest integer. `Scene3D?.Camera.OnViewportSizeChanged()` stays as-is.
4. `Game` needs access to `DpiScale` to do step 3. If it does not already hold the `GameWindow`,
   add a settable `float DpiScale` property on `Game` defaulting to `1.0f`, set by the same code
   that calls `EnsureFrame`. Do not add a new `GameWindow` dependency to `Game`.
5. Add a unit test over the size-splitting logic: given a pixel size and a `DpiScale` of 2, assert
   the viewport size equals the pixel size and the GUI size equals half of it; at `DpiScale` 1
   assert both are equal. Extract the split into an internal static helper if that is needed to
   test it without a window.

## Files likely to change

- `src/OpenSage.Game/Game.cs`
- `src/OpenSage.Game/GamePanel.cs`
- `src/OpenSage.Launcher/Program.cs`
- `src/OpenSage.Tools.TestHarness/Program.cs`
- `src/OpenSage.Game.Tests/`: new unit test.

## Acceptance criteria

- [ ] All three `EnsureFrame` call sites pass `DrawableBounds`.
- [ ] `Game.Viewport` is in pixels; `WndWindowManager.OnViewportSizeChanged` and
      `AptWindowManager.OnViewportSizeChanged` receive a logical size.
- [ ] The size-split unit test passes at `DpiScale` 1 and 2.
- [ ] The game still launches and renders correctly at scale 1 with no visible change.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not fix 2D drawing scale or mouse picking here. After this card, on a display with scale != 1
  the HUD will draw at the wrong size and picking will be offset. That is expected and is exactly
  what `WI-G1-04` closes. Do not partially work around it.
- Do not change WND/APT layout logic itself; only the size value handed to it.
- Do not rename `GamePanel.ClientBounds` or `Frame`, which would ripple beyond this card.
- If the classification table from `WI-G1-01` lists a consumer not covered by these steps, mark
  `Blocked` and report rather than improvising.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - None; `WI-G1-04` is next and already exists.
