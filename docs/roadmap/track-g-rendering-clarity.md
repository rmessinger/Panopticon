# Track G: Rendering Clarity & Image Quality

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
| [G1. High-DPI drawable & resolution correctness](deep-dives/g1-high-dpi-resolution.md) | Use `AllowHighDpi` and drawable-sized swapchain/render targets. Physical pixels serve rendering, input and proportional GUI layout; logical points remain only at the OS boundary (window creation, OS cursor, ImGui). | Swapchain and captured frame match native window pixels; cursor/input hit-testing stays correct. Scale-2 mapping is unit-tested; real HiDPI display validation remains outstanding. | Done | None |
| [G2. Graphics settings & fidelity presets](deep-dives/g2-graphics-settings-presets.md) | Add applied graphics settings and `Legacy`/`Enhanced` presets with capability fallbacks and safe resource rebuilding. The parsed `StaticGameLod` has no runtime consumer. Settings layer only, not an [experiment seam](seams.md). | Runtime preset switching changes active pipeline state; `Legacy` reproduces the pre-track captured frame. | Done | None |
| [G3. Visual regression harness](deep-dives/g3-visual-regression-harness.md) | Extend `FrameCapturer` and TestHarness `ScenarioCapture` into fixed-scene captures, metrics and a host-pinned golden gate. No new tool, per the [C5 Decision](open-questions.md). | A headless run captures both presets, compares stored goldens and fails on regression; metrics distinguish aliased from antialiased silhouettes. | Done (six scenes captured byte-identically under a fixed render clock; goldens in an external host-pinned store behind `TESTHARNESS_VR_UPDATE_GOLDENS`, committed manifest with per-golden SHA-256) | G1, G2 |
| [G4. Multisample antialiasing](deep-dives/g4-msaa.md) | Thread supported Off/2x/4x/8x sample counts through 3D targets and pipelines. Resolve before single-sample 2D and the final blit. `Legacy` defaults off. | 4x under `Enhanced` renders and resolves across all main passes; silhouette EdgeAliasing 0.0725 -> 0.0707 vs 1x; `Legacy` golden-identical. | Done | G2, G3 |
| [G5. Cutout & alpha-test clarity](deep-dives/g5-alpha-test-clarity.md) | Audit cutout roles and actual mip provenance: generated mips already use Lanczos3 and DDS can carry authored mips. Add coverage-preserving alpha rescale and alpha-to-coverage under MSAA without assuming the cause of every dotted silhouette. | Minified foliage/fence silhouettes remain intact under `Enhanced`; `Legacy` preserves its original hard-cutoff result. | Not started | G4 |
| [G6. Gamma-correct rendering](deep-dives/g6-gamma-correct-rendering.md) | Decode colour textures to linear space, filter colour mips in linear space, shade linearly and apply exactly one sRGB output transfer with deterministic ordered dither. Keep data textures linear. | `Enhanced` gradient captures meet the banding check and colour mips stop darkening; `Legacy` retains gamma-space shading. | Not started | G2, G3 |
| [G7. Texture filtering & mip quality](deep-dives/g7-texture-filtering.md) | Replace fixed 4x anisotropy with capability-aware settings up to 16x and bounded mip bias. Audit the existing Lanczos3 terrain-array resampling path before changing it. | Grazing-angle textures stay legible and fixed camera-motion captures meet the temporal shimmer check. | Not started | G2, G3 |
| [G8. Shadow quality tier](deep-dives/g8-shadow-quality.md) | Select the existing four-cascade path at 2048² under `Enhanced`; add bounded receiver slope bias while retaining normal offset and original defaults. | Default-zoom shadows show no acne, visible cascade seams or detached contacts; `Legacy` matches its golden. | Not started | G2, G3 |
| [G9. Resolution-independent 2D & UI](deep-dives/g9-resolution-independent-2d.md) | Preserve G1's proportional physical-pixel layout. Add role-aware sampling and pixel-sized fonts, then dynamic control-bar chrome per the [HUD Decision](open-questions.md). `Legacy` retains legacy art. | Layout and hit regions stay unchanged; `Enhanced` chrome is sharp without legacy panel/button/frame bitmap draws; portraits/icons/minimap remain textures; `Legacy` matches its golden. | Not started | G1, G2, G3 |
| [G10. Realistic proportions](deep-dives/g10-realistic-proportions.md) | Re-derive asset reference scale and aircraft deviations, then add explicit opt-in per-definition render-only scaling. Shared world/bone matrices, `Geometry`, gameplay selection and `AttackRange` remain unchanged; see the [scope Decision](open-questions.md). | Comanche/MiG beside Crusader meet audited real-world length ratios within stated tolerance; disabled `Legacy` stays unchanged; a data test re-derives the asset table. | Not started | G2, G3 |

## Phase resources

A navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams are required before any phase's first source-edit task card is marked `Ready`.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| G1 | [`g1-high-dpi-resolution.md`](deep-dives/g1-high-dpi-resolution.md) | [Architecture Diagrams](deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams) | [`WI-G1-01`](agent/cards/WI-G1-01-audit-size-consumers.md), [`WI-G1-02`](agent/cards/WI-G1-02-allow-highdpi-drawable-size.md), [`WI-G1-03`](agent/cards/WI-G1-03-route-render-surfaces-to-pixels.md), [`WI-G1-04`](agent/cards/WI-G1-04-scale-2d-and-picking.md) |
| G2 | [`g2-graphics-settings-presets.md`](deep-dives/g2-graphics-settings-presets.md) | [Architecture Diagrams](deep-dives/g2-graphics-settings-presets.md#5-architecture-diagrams) | [`WI-G2-01`](agent/cards/WI-G2-01-audit-render-state-knobs.md), [`WI-G2-02`](agent/cards/WI-G2-02-graphics-settings-type-and-presets.md), [`WI-G2-03`](agent/cards/WI-G2-03-wire-settings-into-render-construction.md) |
| G3 | [`g3-visual-regression-harness.md`](deep-dives/g3-visual-regression-harness.md) | [Architecture Diagrams](deep-dives/g3-visual-regression-harness.md#5-architecture-diagrams) | [`WI-G3-01`](agent/cards/WI-G3-01-audit-frame-capture-paths.md), [`WI-G3-02`](agent/cards/WI-G3-02-image-quality-metrics.md), [`WI-G3-03`](agent/cards/WI-G3-03-golden-frame-comparison.md), [`WI-G3-04`](agent/cards/WI-G3-04-golden-store-and-gate.md) |
| G4 | [`g4-msaa.md`](deep-dives/g4-msaa.md) | [Architecture Diagrams](deep-dives/g4-msaa.md#5-architecture-diagrams) | [`WI-G4-01`](agent/cards/WI-G4-01-inventory-sample-count-sites.md), [`WI-G4-02`](agent/cards/WI-G4-02-thread-sample-count-and-resolve.md) |
| G5 | [`g5-alpha-test-clarity.md`](deep-dives/g5-alpha-test-clarity.md) | [Architecture Diagrams](deep-dives/g5-alpha-test-clarity.md#5-architecture-diagrams) | [`WI-G5-01`](agent/cards/WI-G5-01-audit-alpha-test-materials.md), [`WI-G5-02`](agent/cards/WI-G5-02-alpha-to-coverage-and-cutout-mips.md) |
| G6 | [`g6-gamma-correct-rendering.md`](deep-dives/g6-gamma-correct-rendering.md) | [Architecture Diagrams](deep-dives/g6-gamma-correct-rendering.md#5-architecture-diagrams) | [`WI-G6-01`](agent/cards/WI-G6-01-audit-colour-space-handling.md), [`WI-G6-02`](agent/cards/WI-G6-02-linear-space-shading-and-dither.md) |
| G7 | [`g7-texture-filtering.md`](deep-dives/g7-texture-filtering.md) | [Architecture Diagrams](deep-dives/g7-texture-filtering.md#5-architecture-diagrams) | [`WI-G7-01`](agent/cards/WI-G7-01-audit-samplers-and-mip-paths.md), [`WI-G7-02`](agent/cards/WI-G7-02-configurable-anisotropy-and-mip-bias.md) |
| G8 | [`g8-shadow-quality.md`](deep-dives/g8-shadow-quality.md) | [Architecture Diagrams](deep-dives/g8-shadow-quality.md#5-architecture-diagrams) | [`WI-G8-01`](agent/cards/WI-G8-01-audit-shadow-settings-flow.md), [`WI-G8-02`](agent/cards/WI-G8-02-four-cascade-tier-and-slope-scaled-bias.md) |
| G9 | [`g9-resolution-independent-2d.md`](deep-dives/g9-resolution-independent-2d.md) | [Architecture Diagrams](deep-dives/g9-resolution-independent-2d.md#5-architecture-diagrams) | [`WI-G9-01`](agent/cards/WI-G9-01-audit-2d-ui-resolution-path.md), [`WI-G9-02`](agent/cards/WI-G9-02-native-resolution-2d-layer.md), [`WI-G9-03`](agent/cards/WI-G9-03-dynamic-hud-chrome.md) |
| G10 | [`g10-realistic-proportions.md`](deep-dives/g10-realistic-proportions.md) | [Architecture Diagrams](deep-dives/g10-realistic-proportions.md#5-architecture-diagrams) | [`WI-G10-01`](agent/cards/WI-G10-01-audit-proportions-and-reference-scale.md), [`WI-G10-02`](agent/cards/WI-G10-02-presentation-scale-overrides.md), [`WI-G10-03`](agent/cards/WI-G10-03-preset-wiring-and-golden-frames.md) |

## Cross-track dependencies

Hard dependencies are in the phase table above; these are the coordination notes:

- Track G is not gated on Track P. It touches presentation only and changes no simulation
  behaviour, so it does not need a playable skirmish loop; see the
  [rendering-clarity scope Decision](open-questions.md).
- G4/G6/G7 change pipeline and render-target descriptions that [B5 rendering at scale](track-b-scale.md)
  also rewrites for instancing and batching. Whichever lands first owns the pipeline-description
  plumbing; the other builds on it rather than forking a second path.
- G3 extends the TestHarness `ScenarioCapture` runner, with scene profiles in the
  [P0 ReplaySketch harness](deep-dives/p0-playable-baseline-harness.md) profile store, and the
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
