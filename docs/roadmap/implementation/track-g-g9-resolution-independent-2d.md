# G9 Implementation: Resolution-Independent 2D

> Audience: agent. Card sequence and evidence for [G9](../deep-dives/g9-resolution-independent-2d.md).

## Scope

Preserve G1's pixel-space proportional layout; improve text/art sampling and replace HUD chrome.
Source of truth: [design and diagrams](../deep-dives/g9-resolution-independent-2d.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| WI-G9-01 | Draw/font/hit-region table and chrome-role manifest | none |
| WI-G9-02 | Native-pixel text/borders and role-aware sampling | WI-G9-01, WI-G1-04, WI-G2-03, WI-G3-03 |
| WI-G9-03 | Dynamic chrome callbacks and state-complete captures | WI-G9-02 |

## Audit (WI-G9-01, complete)

Produced by WI-G9-01 against `a49a770ed`. Read-only; no `.cs` modified.

### Size trace, window to framebuffer

| Hop | Code | Space |
|---|---|---|
| Window client area | `GameWindow.ClientBounds` (SDL bounds) | logical points, the only points in the chain |
| Drawable area | `GameWindow.DrawableBounds` (`Sdl2Interop.GetWindowSizeInPixels`) | physical pixels |
| DPI factor | `GameWindow.DpiScale` (`DpiScaling.GetScale`) | pixels per point |
| Mouse conversion | `GameWindow.cs` `DpiScaling.ToPixels(state.X, DpiScale)` | one conversion at the OS boundary; every downstream consumer sees pixels |
| Panel | `GamePanel.ClientBounds` = swapchain `Frame.Width/Height` | physical pixels |
| Managers | `Game.OnPanelSizeChanged` passes `Panel.ClientBounds.Size` to `WndWindowManager.OnViewportSizeChanged`, `AptWindowManager.OnViewportSizeChanged` and `Camera.OnViewportSizeChanged` | physical pixels |
| WND layout | `Window.OnSizeChanged` computes `_rootTransform = RectangleF.CalculateTransformForRectangleFittingAspectRatio(Bounds, _creationResolution, newSize)` | layout transform from authored to current pixels, aspect-fit; `_rootTransformInverse` serves input |
| WND authored size | `wndFile.RootWindow.ScreenRect.CreationResolution` (`Window.cs` ctor chain) | authored pixels (typically 800x600) |
| APT layout | `AptWindow.Layout` stores `_destinationSize = windowSize`; `GetScaling() = _destinationSize / _movieSize` | division of current pixels by `Movie.ScreenWidth/Height` |
| APT scaling application | `AptRenderingContext.CalculateTransform` multiplies each item's `GeometryRotation.M11/M22` and `GeometryTranslation` by the window scaling | sampler-independent transform applied per item; `BackgroundImage` is drawn to the full window rectangle (stretch, not aspect-fit) |
| 2D pass size | `DrawingContext.Begin(..., new SizeF(context.RenderTarget.Width, context.RenderTarget.Height), ...)` | swapchain pixels |

No intermediate upscale target exists on the main path. APT renders clip masks to offscreen
`RenderTarget`s through a second `DrawingContext2D` (`AptRenderingContext.SetRenderTarget`).

### 2D sampler

`_loadContext.StandardGraphicsResources.LinearClampSampler`, passed at `RenderPipeline.Execute`'s
`DrawingContext.Begin` call. Linear filtering, clamp addressing, shared with the APT clip-mask
context. This single sampler filters every scaled 2D bitmap; it is the softening stage for
authored art and the site a role-aware policy (`WI-G9-02`) replaces.

### Text rasterization

`DrawingContext2D.DrawText` already rasterizes at effective pixel size:
`_contentManager.FontManager.GetOrCreateFont(font.Name, font.Size * _currentScale, ...)`, with
`_currentScale` derived from the current transform (`sqrt(M11^2 + M21^2)`). `TextCache` caches
glyph-run textures by text, font, alignment, colour and pixel size, with expiry. Border snapping
(`DrawRectangle` scaling by `_currentScale`) exists alongside.

### Hit regions

| Site | Mechanism |
|---|---|
| `WndInputMessageHandler.cs` mouse move and click handlers | `WndWindowManager.GetControlAtPoint` (topmost window first) + `Control.PointToClient` |
| `Control.cs` `PointToClient`/`HitTest`/`GetSelfOrDescendantAtPoint` | recursive parent chain in creation-resolution space; `Window.PointToClient` applies `_rootTransformInverse` |
| `ComboBox.cs` overrides | owner-drawn dropdown hit test in the same space |
| `AptInputMessageHandler.cs` move/down/up | `AptWindow.HandleInput` pixel coordinates |
| `SpriteItem.HandleInput` | reverse-depth content walk |
| `ButtonItem.HandleInput` | authored triangle vertices transformed by the current (scaled) item transform, tested against pixel-space mouse |

All sites stay aligned with the proportional layout because the transforms used for hit testing
are the same matrices used for drawing. `WI-G9-02` must keep that invariant.

### 2D render target

Definitive: the 2D pass never draws into the swapchain. It draws into the single-sample
intermediate (`Legacy`) or, under `Enhanced`, into the composition target after the MSAA resolve.
Presentation then goes through `TextureCopier` (`Legacy`) or `OutputColourPass` (`Enhanced`,
which applies the sRGB encode and dither to composited 2D content; 2D tints are decoded at
`SpriteBatch.DrawImage` per G6). G4's resolve therefore precedes 2D and G6's transfer follows it.

### Chrome-role groundwork for WI-G9-03

WND bitmaps load through `ImageLoader` over `ImageTextureCache` (`Gui/Wnd/Images/`), backed by
`GuiTextureAsset`/`OnDemandGuiTextureLoader` (sRGB uploads and linear mip chains under `Enhanced`
since G6). The Generals control bar chrome draws from `Panopticon.Mods.Generals/Gui/ControlBarCallbacks.cs`
(static callback class). The manifest of forbidden-under-`DynamicHud` draws belongs to `WI-G9-02`'s
inventory work; this audit fixes the pipeline facts it builds on.

## Validation and risks

Scale-1/2 and widescreen bounds tests preserve input and layout. Enhanced draws no legacy chrome
bitmaps; raster content remains allowed. Legacy is exact. Check all button states and faction
variants. ReplaySketch UI changes require screenshot-mode verification of map and model tabs.
