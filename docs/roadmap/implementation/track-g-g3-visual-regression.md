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

## Audit deliverables (`WI-G3-01`, to complete)

- Capture path table: entry point, texture captured, includes HUD or not, output location,
  persistence.
- Scene repeatability table, one row per candidate scene: profile, camera, settle frames, capture
  frame, result of two consecutive captures (byte-identical or not), and the effect responsible
  for any difference.
- Reference host record: backend, device name, driver version, as reported by the graphics device.

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
