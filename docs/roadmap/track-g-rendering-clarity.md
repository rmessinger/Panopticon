# Track G — Rendering Clarity & Image Quality

> Part of the [Large-Scale RTS Roadmap](README.md). Image-quality work on the existing forward
> renderer: correct high-DPI output, antialiasing, cutout/alpha-test clarity, gamma-correct
> shading, texture filtering, shadow quality, resolution-independent 2D, and optional realistic
> proportions. The current renderer
> reproduces the original game's look but is grainy on high-resolution displays, with a
> screen-door pattern on alpha-tested geometry. This track fixes the correctness bugs behind that
> and adds clarity beyond original SAGE, behind a preset that preserves the original look (see the
> [rendering-clarity scope Decision](open-questions.md)).
>
> Audience: human; strategic track overview.

## Phases

> The Milestone column states the single demonstrable outcome that marks each phase done (its
> definition-of-done); the granular steps to get there live in
> [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| G1. High-DPI drawable & resolution correctness | Render at the display's native pixel resolution. `GameWindow` never sets `SDL_WINDOW_ALLOW_HIGHDPI`, and sizes the swapchain and render target from `Sdl2Window.Bounds` (logical points), while separately multiplying the requested window size by `WindowScale`. On a scaled display the whole frame is therefore rendered below native resolution and resampled by the compositor. Establish one convention: pixels for rendering surfaces, logical points for input and UI layout. | On a display with a non-unity scale factor, the swapchain and captured frame match the native pixel dimensions of the window, and cursor/input hit-testing stays correct at that scale. | Not started | — |
| G2. Graphics settings & fidelity presets | Introduce an engine-level graphics-settings layer (sample count, alpha-test mode, colour-space mode, anisotropy, mip bias, shadow tier) with `Faithful` and `Enhanced` presets, so later phases are switchable rather than unconditional. The parsed `StaticGameLod` assets already name several of these knobs but have no runtime consumer, so G2 builds the application layer rather than extending one. Settings layer only, not an [experiment seam](seams.md). | Switching between `Faithful` and `Enhanced` at runtime changes the active pipeline state, and `Faithful` reproduces the pre-track output on a captured frame. | Not started | — |
| G3. Visual regression harness | Extend the existing `FrameCapturer`/`FrameCaptureService` and ReplaySketch screenshot mode into a golden-frame comparison over a fixed scene set, per preset, with objective sharpness and edge-aliasing metrics rather than eyeball checks. No new tool, per the [C5 no-dedicated-viewer Decision](open-questions.md). | A headless run captures the fixed scene set under both presets, compares against stored goldens, and fails on regression; the metrics distinguish an aliased frame from an antialiased one. | Not started | G1, G2 |
| G4. Multisample antialiasing | Thread a sample count through `RenderPipeline.GameOutputDescription`, the intermediate colour/depth targets, and every pipeline description, with an MSAA resolve before the final `TextureCopier` blit. Off/2x/4x/8x, defaulting to off under `Faithful`. | 4x MSAA renders and resolves correctly across the 3D, terrain, water, road and particle pipelines, and the G3 edge-aliasing metric improves measurably against the same frame at 1x. | Not started | G2, G3 |
| G5. Cutout & alpha-test clarity | Remove the screen-door pattern on alpha-tested geometry (foliage, tree cards, fences, chain-link). `Common.h`'s `FailsAlphaTest` discards below a fixed `0x60/0xFF` with no coverage preservation, so box-filtered mip chains thin cutout alpha until minified cutouts dissolve into a dotted lattice. Add alpha-to-coverage when MSAA is active, coverage-preserving mip generation for alpha-tested textures, and a per-mip threshold rescale. | A scene with minified alpha-tested foliage and fencing shows no dissolve-to-dots at distance under `Enhanced`, with cutout silhouettes preserved and `Faithful` still producing the original hard-cutoff result. | Not started | G4 |
| G6. Gamma-correct rendering | Move shading to linear space: sRGB texture views for colour maps, linear lighting, sRGB or tonemapped output, mip filtering in linear space (`MipmapHelper` currently resamples gamma-space data), and an ordered dither before the 8-bit output to remove banding on sky, fog and light falloff. | Under `Enhanced`, a captured frame shows no visible banding on a fog/sky gradient and mip chains no longer darken with distance; `Faithful` retains gamma-space shading unchanged. | Not started | G2, G3 |
| G7. Texture filtering & mip quality | Anisotropy is hardcoded at 4x (`StandardGraphicsResources.Aniso4xClampSampler`, built from `SamplerDescription.Aniso4x`) with no LOD bias control. Make anisotropy configurable up to 16x, add a mip LOD bias, and raise the quality of the terrain texture-array resample/mip path in `Terrain.cs`. | Terrain and ground-plane textures stay legible at grazing angles at 16x with no shimmer under camera motion, measured by the G3 metrics. | Not started | G2, G3 |
| G8. Shadow quality tier | `ShadowSettings` defaults to `OneCascade` at 1024² over a 1200-unit shadow distance with a constant depth bias, producing blocky, speckling shadow edges at high resolution. Default `Enhanced` to four cascades at 2048², and replace the constant bias with slope-scaled/normal-offset bias. | Shadow edges under `Enhanced` show no acne speckle or visible cascade blockiness at the default camera zoom, with cascade transitions not visible in the captured frame. | Not started | G2, G3 |
| G9. Resolution-independent 2D & UI | The 2D layer draws 800x600-era WND/APT art through a linear clamp sampler scaled to the framebuffer, so the HUD softens as resolution rises. Render 2D at native resolution with resolution-independent layout and higher-quality upscaling, keeping layout anchored in logical points from G1, then rework the HUD itself: replace the pre-rendered 800x600 bitmaps (control bar, buttons, frames) with dynamically drawn equivalents (vector/procedural geometry, live text) so the HUD is genuinely resolution-independent rather than upscaled legacy art, per the [hud-rework scope Decision](open-questions.md). `Faithful` keeps the original bitmaps. | The control bar and HUD text stay sharp at native resolution on a scaled display, with layout and hit regions unchanged relative to the pre-track build; under `Enhanced`, the control bar renders from dynamic geometry with no pre-rendered HUD bitmaps in the draw list, and `Faithful` is pixel-identical to the pre-rework capture. | Not started | G1, G2, G3 |
| G10. Realistic proportions | Optional scale-correct presentation, grounded in measured asset data. Infantry, ground vehicles, and civilian/faction buildings are already mutually proportionate at roughly 5 world units per meter (terrain cell 10 units ≈ 2 m), but aircraft models are undersized roughly 2-2.5x versus real counterparts (measured from W3D mesh bounds with FX meshes excluded, cross-checked against INI `Geometry`; e.g. MiG ≈ 6.6 m in-game versus ≈ 17.3 m real). Document the reference scale, and add per-definition visual model-scale overrides correcting the aircraft class behind the G2 settings layer. Presentation only: `Geometry` collision boxes (selection, pathing) and `AttackRange` values (compressed ≈ 100x as deliberate gameplay data) are untouched; see the [realistic-proportions scope Decision](open-questions.md). | With the proportions option enabled, a Comanche and MiG parked next to a Crusader match their real-world length ratios within a stated tolerance in a captured frame, while `Faithful` output is unchanged; a checked-in data test re-derives the deviation table from installed INI/W3D assets so a changed asset fails it. | Not started | G2, G3 |

## Phase resources

A navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams are required before any phase's first task card is marked `Ready`; deep dives for G2
onward are yet to be written.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| G1 | [`g1-high-dpi-resolution.md`](deep-dives/g1-high-dpi-resolution.md) | [Architecture Diagrams](deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams) | [`WI-G1-01`](agent/cards/WI-G1-01-audit-size-consumers.md), [`WI-G1-02`](agent/cards/WI-G1-02-allow-highdpi-drawable-size.md), [`WI-G1-03`](agent/cards/WI-G1-03-route-render-surfaces-to-pixels.md), [`WI-G1-04`](agent/cards/WI-G1-04-scale-2d-and-picking.md) |
| G2 | — | — | [`WI-G2-01`](agent/cards/WI-G2-01-audit-render-state-knobs.md), [`WI-G2-02`](agent/cards/WI-G2-02-graphics-settings-type-and-presets.md), [`WI-G2-03`](agent/cards/WI-G2-03-wire-settings-into-render-construction.md) |
| G3 | — | — | [`WI-G3-01`](agent/cards/WI-G3-01-audit-frame-capture-paths.md), [`WI-G3-02`](agent/cards/WI-G3-02-image-quality-metrics.md), [`WI-G3-03`](agent/cards/WI-G3-03-golden-frame-comparison.md) |
| G4 | — | — | [`WI-G4-01`](agent/cards/WI-G4-01-inventory-sample-count-sites.md), [`WI-G4-02`](agent/cards/WI-G4-02-thread-sample-count-and-resolve.md) |
| G5 | — | — | [`WI-G5-01`](agent/cards/WI-G5-01-audit-alpha-test-materials.md), [`WI-G5-02`](agent/cards/WI-G5-02-alpha-to-coverage-and-cutout-mips.md) |
| G6 | — | — | [`WI-G6-01`](agent/cards/WI-G6-01-audit-colour-space-handling.md), [`WI-G6-02`](agent/cards/WI-G6-02-linear-space-shading-and-dither.md) |
| G7 | — | — | [`WI-G7-01`](agent/cards/WI-G7-01-audit-samplers-and-mip-paths.md), [`WI-G7-02`](agent/cards/WI-G7-02-configurable-anisotropy-and-mip-bias.md) |
| G8 | — | — | [`WI-G8-01`](agent/cards/WI-G8-01-audit-shadow-settings-flow.md), [`WI-G8-02`](agent/cards/WI-G8-02-four-cascade-tier-and-slope-scaled-bias.md) |
| G9 | — | — | [`WI-G9-01`](agent/cards/WI-G9-01-audit-2d-ui-resolution-path.md), [`WI-G9-02`](agent/cards/WI-G9-02-native-resolution-2d-layer.md), [`WI-G9-03`](agent/cards/WI-G9-03-dynamic-hud-chrome.md) |
| G10 | — | — | — |

## Cross-track dependencies

Hard dependencies are in the phase table above; these are the coordination notes:

- Track G is not gated on Track P. It touches presentation only and changes no simulation
  behaviour, so it does not need a playable skirmish loop; see the
  [rendering-clarity scope Decision](open-questions.md).
- G4/G6/G7 change pipeline and render-target descriptions that [B5 rendering at scale](track-b-scale.md)
  also rewrites for instancing and batching. Whichever lands first owns the pipeline-description
  plumbing; the other builds on it rather than forking a second path.
- G3 extends the [P0 ReplaySketch harness](deep-dives/p0-playable-baseline-harness.md) and the
  existing `FrameCapturer` rather than adding a tool, and its golden frames are the regression
  guard for [Track F's](track-f-terrain-destruction.md) F2/F8 terrain-rendering changes.
- G1 shares the window/DPI surface with [A3 sim/presentation seam](track-a-foundations.md): the
  pixel-versus-point split must sit on the presentation side of that seam so the headless host is
  unaffected.
- G2's settings layer is not an [experiment seam](seams.md); it selects fixed pipeline state
  rather than swappable measured implementations, so it stays out of the A4 harness.
- G10's scale overrides are presentation-side only and must not cross the
  [A3 sim/presentation seam](track-a-foundations.md): the headless host and every simulation
  assertion run identically with the option on or off. The overrides also stay per-definition
  visual state rather than asset mutation, so `Geometry`-driven selection and pathing consumers
  (`Scene25D.GetBoundingSphere`) are unaffected.
