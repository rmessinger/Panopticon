# Panopticon ReplaySketch (Avalonia) — Agent Guide

## Purpose

`Panopticon.Tools.ReplaySketch.Avalonia` is the interactive front-end for ReplaySketch: the
ordered command/scenario authoring and validation tool. It replaced the former MAUI
front-end (removed) and runs on Linux, macOS, and Windows.

## Architecture

| Project | Contains |
|---|---|
| `Panopticon.Tools.ReplaySketch.Core` | All logic: scenario/command model, exporter, services, renderers, controllers, view models' services |
| `Panopticon.Tools.ReplaySketch.Avalonia` | UI only: `MainWindow`, views, `MainViewModel` presentation wiring |

Rule of thumb: if you are adding behaviour, put it in `.Core` so it is covered by a headless
test in `Panopticon.Tools.ReplaySketch.Tests`. The Avalonia layer stays a thin shell that binds
views to `.Core` types and services (dispatch, pickers, process launch) behind the Core
interfaces.

Skia drawing goes through `Controls/SkiaCanvas`, which leases Avalonia's own Skia surface so
the Core renderers (`MapPreviewRenderer`, `ActionTreeRenderer`, `ValidationGroundMapRenderer`)
draw unchanged in DIP space.

## Commands

Run from the repository root.

```bash
dotnet build src
dotnet test test/Panopticon.Tools.ReplaySketch.Tests
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Screenshot mode

Environment variables before launching (mirrors the previous MAUI contract):

| Variable | Default | Effect |
|---|---|---|
| `REPLAYSKETCH_SCREENSHOT_PATH` | (none; mode inactive) | Absolute path to write the output PNG. Activates screenshot-and-exit mode. |
| `REPLAYSKETCH_SCREENSHOT_TAB` | `tree` | Which tab to capture: `tree`, `map`, `validationground`, `model`, or `harness`. |
| `REPLAYSKETCH_SCREENSHOT_DELAY` | `3` | Seconds to wait after open before capturing. Increase for map loads. |

On headless Linux hosts, run under Xvfb with software GL (see
[`docs/screenshots.md`](../../docs/screenshots.md) for the verified recipe). `model`-tab
captures use the deterministic `ExternalModelScreenshotFixture` (fake session, stable preview
image, no TestHarness process) and need no game install; `map` captures need
`CNC_GENERALS_PATH`.

The older `--screenshot <path>` CLI argument still renders the validation-ground editor scene
headlessly through SkiaSharp with no display at all; it is separate from the window capture.

## Validation expectations

When changing the Avalonia layer (layout, view wiring, tab switching), capture the affected
tab plus the Map tab as a regression image, and inspect both PNGs. Pixel-level structural
checks (distinct-color count, region variance) via PIL are documented in
[`docs/screenshots.md`](../../docs/screenshots.md).
