# G3 Implementation: Visual Regression Harness

> Implementation plan for [Track G · G3](../track-g-rendering-clarity.md). Source of truth:
> [deep-dives/g3-visual-regression-harness.md](../deep-dives/g3-visual-regression-harness.md),
> [Architecture diagrams §5](../deep-dives/g3-visual-regression-harness.md#5-architecture-diagrams).
>
> Audience: agent. Card sequence, audit deliverables, and operating notes for the golden-frame gate.

## Scope

Extend TestHarness engine-frame capture (`FrameCapturer`, `ScenarioCaptureRunner`) into a
`VisualRegression` built-in scenario. ReplaySketch screenshot mode (Avalonia
`RenderTargetBitmap`, SkiaSharp `--screenshot`) and `FrameCaptureService` are not extended; see
the deep dive §1 corrections.

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| [`WI-G3-01`](../agent/cards/WI-G3-01-audit-frame-capture-paths.md) | Capture-path gap list, per-scene repeat-run result, reference host record | none |
| [`WI-G3-02`](../agent/cards/WI-G3-02-image-quality-metrics.md) | `ImageQualityMetrics` and synthetic-image tests | none |
| [`WI-G3-03`](../agent/cards/WI-G3-03-golden-frame-comparison.md) | Fixed render clock, camera pitch/angle override, preset and scale in `CaptureRunSettings`, scene set, capture pass, supersampled-reference check | `WI-G3-01`, `WI-G3-02`, `WI-G2-03`, `WI-G1-04` |
| [`WI-G3-04`](../agent/cards/WI-G3-04-golden-store-and-gate.md) | `GoldenFrameComparer`, golden store and manifest, update mode, exit codes, initial goldens | `WI-G3-03` |

`WI-G3-01` and `WI-G3-02` can run in parallel. Later Track G phases validate against goldens only
once `WI-G3-04` is `Done`.

## Audit (`WI-G3-01`, complete)

### Capture paths

| Path | Entry point | Texture captured | Output | Window |
|---|---|---|---|---|
| Engine capture | `FrameCapturer.Capture(GraphicsDevice, Texture)` + `CaptureToPng`; scheduled through `FrameCaptureService.ScheduleCapture`/`FlushPending`/`Capture` | Any GPU texture; engine loop passes `game.Panel.Framebuffer.ColorTargets[0].Target`, the `TextureCopier` final-blit destination in `RenderPipeline` | `$TMPDIR/panopticon_captures/capture_NNNN.png` plus a `[CAPTURE]` stdout line; counter-based naming, per-instance cleanup | No (offscreen device) |
| ScenarioCapture | `ScenarioCaptureRunner.Run` via `--built-in-scenario ScenarioCapture --scenario <profile.json>`; captures `frame % CaptureStride == 0` after `SettleFrames` | Same panel target via `FrameCapturer.CaptureToPng` | `<dir>/frame_NNNN.png` + `manifest.json` per profile; default `$TMPDIR/panopticon_capture_<name>`, overridable via profile `OutputDirectory` or `CaptureRunSettings.FallbackOutputDirectory` | No (`--headless`) |
| ReplaySketch screenshot | `REPLAYSKETCH_SCREENSHOT_PATH` activates `MainWindow.TakeScreenshotAsync` | None from the engine; `RenderTargetBitmap.Render(this)` rasterizes the Avalonia window visual tree | The configured PNG path | Yes (Avalonia window) |

ReplaySketch screenshots are definitively not `FrameCapturer` output: they rasterize the
tool UI through Avalonia Skia, never read a GPU texture, and cannot capture an engine frame.
G3 extends only the engine path, per the C5 Decision.

### Existing headless capture evidence

- The validation-grounds run (`TESTHARNESS_VG_CAPTURE=1 ... --headless`) is the existing
  full-world capture test: 280 PNGs at 1024x768 through `FrameCaptureService`, summary
  "26 measured (0 pass, 0 regressed, 0 new), 4 timed out, 1 gaps". Its per-run directory
  layout is counter-based and instance-scoped, so it cannot serve as the golden store
  unchanged; `ScenarioCaptureRunner`'s profile-keyed `frame_NNNN.png` + `manifest.json`
  layout is the closer model for `visual-regression/<preset>/<scene>.png`.
- `PbrMaterialProof` and `GltfRuntimeProof` already assert byte-identical repeat captures
  at model level, using discarded warm-up frames to absorb first-execution uploads. The
  capture machinery itself is deterministic; repeatability failures below are scene content,
  not readback.

### Scene repeatability (two consecutive runs, same commit)

| Scene approximation | Machinery | Result | Cause |
|---|---|---|---|
| Validation grounds, full animated world, HUD on | `TESTHARNESS_VG_CAPTURE` | 0 of 280 PNGs byte-identical | Free-running render clock; presentation-time animation (water, foliage, particles) advances by wall-clock between runs. Sampled frame: 424314 of 786432 pixels differ, mean delta 1.19, max 142 |
| `capture-car-flipped.json` (Follow camera, settled physics subject, `HideHud`) | `ScenarioCapture` | 0 of 70 PNGs byte-identical | Same render-clock cause in a nearly static scene: sampled frame has 231240 of 786432 pixels differing but max channel delta 3, mean 1.0, so the drift is low-order-bit presentation noise, not structured motion |

Full-world scenes are not byte-identical today. The fixed logic timestep already makes
simulation deterministic; the remaining variance is render-time-driven presentation.
`WI-G3-03`'s `EnableFixedRenderTimestep` is the remediation, after which per-scene identity
is re-measured (its acceptance criteria). `terrain-grazing` additionally needs the G3-03
camera pitch override before it can be measured at all. No candidate scene is dropped on
current evidence; the drop-or-fix decision moves to G3-03 with the fixed clock available.

### Reference host record

Vulkan 1.4.341 instance, device `AMD Radeon RX 7600 (RADV NAVI33)`, driver Mesa radv
26.0.8-1ubuntu0.3, discrete GPU. The goldens manifest (`WI-G3-04`) pins these values from
`GraphicsDevice` at capture time.

### Gap list (owner)

| Missing capability | Owner |
|---|---|
| Fixed render clock for presentation determinism | `Game.EnableFixedRenderTimestep` (`WI-G3-03`) |
| Stable scene set and per-scene capture frame | `visual-regression-scenes.json` + `vr-<scene>.json` profiles (`WI-G3-03`) |
| Camera pitch and angle override | `CaptureCamera.PitchDegrees`/`AngleDegrees` (`WI-G3-03`) |
| Per-preset and per-scale capture dimension | `CaptureRunSettings` preset + `ViewportScale` (`WI-G3-03`) |
| Golden store, manifest and comparer | `visual-regression/`, `GoldenFrameComparer` (`WI-G3-04`) |
| Pass/fail gate exit codes and update mode | `VisualRegressionRunner` (`WI-G3-03` capture pass done; `WI-G3-04` gate) |
| Sharpness and edge-aliasing metrics | `ImageQualityMetrics` (`WI-G3-02`, done) |

## Capture pass (`WI-G3-03`, complete)

- Determinism: `Game.EnableFixedRenderTimestep` pins the render `DeltaTimer` to one fixed step
  per `Update`, matching `EnableFixedLogicTimestep`. With logic already fixed-step, two
  consecutive runs write byte-identical captures for all scenes and presets (verified 28/28 frame
  files on the reference host).
- Camera overrides: `CaptureCamera.PitchDegrees`/`AngleDegrees` apply right after
  `SetAngleAndPitchToDefault`, before height and look-at. `TacticalView.Pitch` composes over the
  INI `CameraPitch` offset; positive degrees rotate toward the horizon (`vr-terrain-grazing` at
  +30 gives the near-horizon view the deep dive requires).
- Scene set: six `vr-*.json` profiles plus `visual-regression-scenes.json`, all embedded through
  the existing `Profiles` glob and loaded via `VisualRegressionSceneSetLoader`. The audit's gap
  table drove the knob list; no scene was dropped for repeatability.
- Capture scheme: `frame_0000` is the warm-up discard, `frame_0001` (`CaptureFrame`) is the
  scoring frame. `DurationFrames` 2, `SettleFrames` 15, fixed render clock on.
- Preset and scale dimensions: `CaptureRunSettings.GraphicsSettings` applies before the scene is
  created (the `Scene3D` constructor reads the shadow tier); `ViewportScale` sizes
  `EnsureFrame`, the camera fit, the safe area and the manifest.
- Supersampled reference: scenes with `SilhouetteRegions` (currently `unit-silhouettes`) also
  capture at scale 2; `FrameDownsampler.Downsample2x2` box-filters the reference back to native,
  and the run fails (exit 5) when the reference does not score lower on edge aliasing over the
  regions. HUD/texture scenes are exempt per the deep dive.
- Output: `<base>/<preset>/<scene>/scale<N>/frame_NNNN.png` plus `report.json`, where `<base>` is
  `TESTHARNESS_VR_OUTPUT_DIR` or `$TMPDIR/panopticon_visual_regression`. Outside the golden store.
- Exit codes: 0 pass, 1 setup failure, 5 silhouette-check failure. Golden gate codes land with
  `WI-G3-04`.
- Metrics are reported as scene-specific heuristics; the fixed thresholds live in the comparer.

## Golden store

- Location: `visual-regression/<preset>/<scene>.png` plus `visual-regression/golden-manifest.json`,
  following the repo-root layout of `pbr-material-proof/` and `gltf-runtime-proof/`.
- Update: `TESTHARNESS_VR_UPDATE_GOLDENS=1` with the `VisualRegression` scenario. The only route.
- Invocation and exit codes: recorded here by `WI-G3-04` when implemented.

## Risks and platform notes

- Goldens are pinned to the measured reference backend, device and driver. A host mismatch exits 6, a
  non-pass result distinct from regression.
- Windowed capture does not work on the dev host; the scenario runs offscreen with `--headless`.
- CI has no install or GPU. Only metric and comparer unit tests run there.
- A golden or repeatability mismatch is a suspected deviation: preserve captures and
  `report.json`, report the failing scene and likely cause, and wait for a decision before
  regenerating.
