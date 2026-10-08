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

## Implementation (WI-G9-02, complete)

The audit found the native-pixel properties already in place: the 2D pass rasterizes into the
frame-resolution intermediate/composition target, `DrawText` selects fonts at
`font.Size * currentScale` with `TextCache` keyed by pixel size, and `DrawRectangle` divides
stroke widths by the current scale after scaling. WI-G9-02 keeps those invariants and adds the
preset-dependent sampling:

- `UiImageSamplingPolicy` (`Gui/UiImageSamplingPolicy.cs`) selects the sampler for mapped
  images: `UiMode.DynamicHud` returns `Aniso4xClampSampler` (settings-driven anisotropy,
  clamp, trilinear), otherwise `null`. Mapped images are the pre-rendered chrome bitmaps
  (control bar pieces, radar, pips, command bar buttons); magnified painted art keeps the
  linear magnification filter, minified chrome gains anisotropic filtering. Text atlas runs
  and non-mapped draws are unaffected.
- `SpriteBatch` gains per-item sampler overrides: `SpriteBatchItem.OverrideSampler`, a
  trailing `samplerOverride` parameter on the quad `DrawImage` overload, and conditional
  resource-set-1 rebinding in `End` tracked against the sampler the pass began with. Null
  overrides emit no additional commands, so Legacy renders byte-identically.
- `DrawingContext2D` resolves the override once at construction and threads it through
  `DrawMappedImage` and both `DrawImage` overloads.
- `GraphicsSettings.Enhanced()` now selects `UiMode.DynamicHud`; `Legacy()` keeps
  `LegacyArt`. G9-03 gates the dynamic chrome on the same member.
- TestHarness: `--viewport-scale N` and `--full-hud` flags on the scenario-capture path
  (`TestHarnessConfig.ViewportScale` into `CaptureRunSettings`) for the scale validation
  below.

## Implementation (WI-G9-03, complete)

Chrome classification happens at image creation, drawing at one switch point:

- `Image.ChromeKind` (`HudChromeKind`: `Panel`, `ButtonNormal/Hover/Pressed/Disabled`,
  `ButtonOverlayHover/Pressed`, `Frame`, `Thumb`, `CheckboxUnchecked/Checked`) tags an image as
  chrome. `ImageLoader` factory methods take the kind as a trailing optional parameter. The WND
  control constructors tag their authored draw data (Button/TextBox/ComboBox/ListBox/Slider/
  CheckBox/RadioButton, and the generic `Control.Wnd` background branch tags `Panel`).
  `GeneralsControlBar` tags mapped-image chrome: scheme toggle-button states
  (`ButtonNormal/Hover/Pressed`), `Cameo_hilited`/`Cameo_push` (`ButtonOverlayHover/Pressed`),
  the `SN/SA/SUEmptyFrame` empty-slot frames (`Frame`), and the scheme's full-width pre-rendered
  bar image (`Panel`). Content stays `None`: cameos, portraits, rank chevrons, and the minimap
  (radar draws through its own path) keep sampling textures under every preset.
- `Image.Draw` is the single switch: when `ChromeKind != None` and
  `DrawingContext2D.DynamicHudChrome` (resolved once from `UiMode.DynamicHud`), the draw
  redirects to `HudChromePainter` instead of sampling. `Legacy` takes the exact prior path, so
  its output is byte-identical.
- `HudChromePainter` draws fills through `FillRectangle` (stretched solid white) and one-pixel
  `DrawPixelSnappedRectangle` borders, so the chrome is vector-crisp at any viewport scale.
  Colours are authored sRGB values; the linear preset decodes them at the sprite level like any
  other 2D tint. Layout and hit regions are untouched: the painter receives the same
  destination rect the bitmap would have occupied.
- `HudChromeAudit` counts chrome-tagged texture draws (`ChromeBitmapDraws`) and procedural
  paints (`DynamicChromePaints`). The visual regression harness resets it before every capture
  and, on full-HUD scenes under the dynamic-HUD preset, fails when chrome bitmaps were sampled
  or when zero procedural paints happened. Compare mode exits 2 on the audit; update mode
  refuses to write the manifest.
- Harness: `VisualRegressionScene.ViewportScale` (default 1) feeds `CaptureRunSettings`; the
  new `hud-4x` scene captures the HUD profile at 4x native (4096x3072) under both presets.
  Captures land in per-scene `scale{N}` directories and the nominal-size drift check applies to
  scale-1 scenes only. Menus share the same WND chrome kinds, so Enhanced menus also draw
  dynamic chrome; APT UI is out of scope and unchanged.

### Evidence (WI-G9-03)

- Legacy proof: pre-regeneration gate passed all seven faithful goldens (including `hud`) and
  stopped at the missing `hud-4x` entry, so `Legacy` output is pixel-identical to pre-card.
- Golden update: 16 goldens regenerated (two new `hud-4x` entries), final gate exit 0 with
  16/16 Passed.
- Injected regression: forcing `DynamicHudChrome` off made the enhanced `hud` and `hud-4x`
  scenes draw 6 chrome-tagged legacy bitmaps (audit failure) and differ on 25.3% / 25.2% of
  pixels against their goldens (exit 2); both the audit and the goldens catch the regression.
- Sharpness at 4x native: control-bar band gradient energy 10.367 (procedural) vs 4.047
  (legacy art upscaled 4x), 2.6x. Comparison image: `panopticon-g9-dynamic-chrome.png` in the
  screenshot viewer.
- Suite: 53 + 355 + 2120 passed, 7 skipped (`Category!=Interactive`); `dotnet format
  --verify-no-changes`, brace check and `git diff --check` clean.
- Known cosmetic: chrome textures are still cached by `Image.SetSize` under dynamic mode but
  never drawn; skipping that fetch is future cleanup.

## Validation and risks

Scale-1/2 and widescreen bounds tests preserve input and layout. Enhanced draws no legacy chrome
bitmaps; raster content remains allowed. Legacy is exact. Check all button states and faction
variants. ReplaySketch UI changes require screenshot-mode verification of map and model tabs.

### Evidence (WI-G9-02)

- Unit tests: `UiImageSamplingPolicyTests` (Enhanced selects the anisotropic clamp sampler,
  Legacy returns null) and the preset test now asserts the `UiMode` difference. Suite:
  53 + 355 + 2120 passed, 7 skipped (`Category!=Interactive`); format, brace and diff checks
  clean. One animation test (`ModelAnimationTests.Bind_OmitsOutOfRangeDirectChannels...`)
  failed once under parallel load and passes standalone and on the full-suite rerun; it is
  unrelated to this card's surface.
- Visual regression gate: exit 0, 14/14 goldens Passed with no regeneration. The anisotropic
  override changes no scene pixels at 1024x768 (HUD chrome is magnified or 1:1 there), which
  also proves the Legacy path byte-identical.
- Scale validation (alpine assault HUD scene, Enhanced, fixed camera): the HUD band starts at
  row 576 of 768 at both viewport scale 1 (1024x768) and scale 2 (2048x1536), exactly 25.0% of
  the frame height, so the HUD occupies the same fraction at both scales. A control-bar text
  crop at native scale 2 measures gradient energy 26.29 against 10.31 for the same crop
  upscaled from the scale-1 capture (2.5x, text rasterized at effective pixel size).
  Comparison image: `panopticon-g9-native-sharpness.png` in the screenshot viewer.
- Hit regions: no layout, transform or hit-testing code changed (the override affects sampling
  only); the Gui test suites pass unchanged.

