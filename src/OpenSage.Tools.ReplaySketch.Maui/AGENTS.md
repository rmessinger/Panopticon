# OpenSAGE ReplaySketch — Agent Guide

## Purpose

`OpenSage.Tools.ReplaySketch` is the primary tool for authoring and validating ordered
command/scenario sequences against the engine. It is the substrate Track P validates through
(see [docs/roadmap/deep-dives/playable-baseline-harness.md](../../docs/roadmap/deep-dives/playable-baseline-harness.md)).

This guide answers one question: how do I reliably validate a ReplaySketch change? It is the
command/scenario-authoring counterpart to
[`OpenSage.Tools.TestHarness/AGENTS.md`](../OpenSage.Tools.TestHarness/AGENTS.md), which covers
renderer visual validation.

## The Core-vs-MAUI split (read this first)

ReplaySketch is two projects, and where a change lives decides how you validate it:

| Project | Contains | How to validate |
|---|---|---|
| `OpenSage.Tools.ReplaySketch.Core` | All logic: scenario/command model, exporter, services, renderers, controllers | Headless unit/golden tests in `OpenSage.Tools.ReplaySketch.Tests` and `OpenSage.Game.Tests` — no MAUI runtime, runs in CI |
| `OpenSage.Tools.ReplaySketch.Maui` | UI only: XAML views, `MainViewModel`, `MauiProgram` | Mostly not headlessly validatable today; keep new logic in `.Core` (see [MAUI seams](#the-maui-layer-what-is-and-is-not-validatable)) |

Rule of thumb: if you are adding behaviour, put it in `.Core` so it is covered by a headless
test. The MAUI layer should stay a thin shell that binds the view to `.Core`.

---

## Commands

Run from the repository root.

```bash
# Build everything (includes both ReplaySketch projects)
dotnet build src

# Run the headless ReplaySketch test suites (no game install or MAUI runtime needed)
dotnet test src/OpenSage.Tools.ReplaySketch.Tests
dotnet test src/OpenSage.Game.Tests --filter "FullyQualifiedName~ReplaySketch"

# Or run everything non-interactive (what CI runs)
dotnet test src --filter Category!=Interactive

# Verify formatting (enforced by CI) / fix it
dotnet format src --verify-no-changes
dotnet format src
```

Note: the `.Maui` project only builds with the platform MAUI workload for the current OS
(maccatalyst on macOS, windows on Windows; it compiles as a plain library on Linux). The
`.Core` and `.Tests` projects build and run on any OS without that workload.

---

## MAUI screenshot mode

When a change touches the MAUI layer (XAML layout, view wiring, tab switching, error display,
`MainViewModel` presentation logic), run the app in screenshot-and-exit mode to capture what it
renders without a human manually launching it.

### How it works

Set `REPLAYSKETCH_SCREENSHOT_PATH` before launching the app. After the page finishes loading and
a settle delay expires, the app captures a full-window screenshot, saves it to the path, and
exits automatically.

```bash
# Capture the Tree tab (default) — app renders, saves, exits
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/replaysketch_tree.png \
  dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui

# Capture the Map tab
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/replaysketch_map.png \
REPLAYSKETCH_SCREENSHOT_TAB=map \
  dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui

# Use a longer settle delay (seconds, default 3) if the map or data is slow to load
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/replaysketch_tree.png \
REPLAYSKETCH_SCREENSHOT_DELAY=6 \
  dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
```

After the command returns, open the PNG:

```bash
open /tmp/replaysketch_tree.png    # macOS: Preview
```

Or view it with the `view` tool inside an agent session.

### Environment variables

| Variable | Default | Effect |
|---|---|---|
| `REPLAYSKETCH_SCREENSHOT_PATH` | (none; mode inactive) | Absolute path to write the output PNG. Activates screenshot-and-exit mode. |
| `REPLAYSKETCH_SCREENSHOT_TAB` | `tree` | Which tab to capture: `tree` or `map`. |
| `REPLAYSKETCH_SCREENSHOT_DELAY` | `3` | Seconds to wait after `OnLoaded` before capturing. Increase if map preview is slow. |

If capture is not supported on the platform, or an error occurs, the app writes a diagnostic to
`<path>.error` instead of the PNG and still exits. Always check for the `.error` file if the
PNG is missing.

### What the screenshot shows without game files installed

Without a game installation detected, `MainViewModel.Initialize()` sets an error/empty state,
so the screenshot captures the app's initial UI with no map data. This is still useful for
verifying layout changes, error states, and tab-switching behaviour. To load real data, set the
game installation path in preferences before launching (or use the `IPreferencesStore` seam once
P0-1b lands).

### Screenshot mode implementation note (P0-1a)

The screenshot mode is implemented in `MainPage.OnLoaded`. It uses
`Microsoft.Maui.Media.Screenshot.Default.CaptureAsync()`, which is supported on maccatalyst and
Windows (the two platforms the app targets). The implementation is in:
`src/OpenSage.Tools.ReplaySketch.Maui/MainPage.xaml.cs`.

---

## What the tests cover

`OpenSage.Tools.ReplaySketch.Tests` (xUnit, non-MAUI):

| Test file | Covers |
|---|---|
| `ActionTreeControllerTests` | Action-tree interaction logic (selection, drag, node layout) |
| `ActionTreeRendererTests` | `SimulateTap` hit-testing against the layout engine's node geometry |
| `GeneralsInstallationServiceTests` | Installation discovery/path service |

`OpenSage.Game.Tests/Tools/ReplaySketch/ReplaySketchTests.cs` covers the command model and the
exporter (scenario → engine `Order` → `.rep`).

---

## The MAUI layer: what is and is not validatable

`MainViewModel` (`ViewModels/MainViewModel.cs`) uses injectable seams for the MAUI statics:

| Binding | Where | Roadmap seam to remove it |
|---|---|---|
| `Preferences.Default` | constructor + `LoadMapFromFolder` | `IPreferencesStore` (`MauiPreferencesStore` / `MemoryPreferencesStore`) |
| `MainThread.InvokeOnMainThreadAsync` | map-load and map-discovery apply-results | `IMainThreadDispatcher` (`MauiMainThreadDispatcher` / `SynchronousMainThreadDispatcher`) |
| `Process.Start` / launcher | `ExecutePlayInGame` | `IProcessLauncher` (`SystemProcessLauncher` / `RecordingProcessLauncher`) |

Do not add new logic directly inside `MainViewModel` methods that touch MAUI/runtime APIs.
Put the logic in a `.Core` service and have the ViewModel call it. When you touch a flow with a seam, add a unit test in
`OpenSage.Tools.ReplaySketch.Tests` using the in-memory/synchronous test double.

Headless run-and-assert entry point:

```sh
dotnet run --project src/OpenSage.Tools.TestHarness -- --run-assert --scenario <path-to-scenario.json> [--game-path <path-to-generals>]
```

This command emits a machine-readable JSON report to stdout and exits 0 when all assertions pass,
1 when any assertion fails.

---

## How agents should validate a ReplaySketch change

1. Identify where your change lives (`.Core` vs `.Maui`) using the split table above.
2. Build: `dotnet build src`.
3. Run the headless suites:
   `dotnet test src/OpenSage.Tools.ReplaySketch.Tests` and the `ReplaySketch` filter on
   `OpenSage.Game.Tests`.
4. If you changed `.Core` behaviour, add or extend a headless test that asserts it.
5. If you changed exporter/command behaviour, validate the produced `.rep` / orders via the
   command-model tests and the run-and-assert entry point.
6. If you changed anything visual — renderer logic, XAML layout, views, tab switching,
   `MainViewModel` presentation — run the screenshot mode and inspect the output PNG to confirm
   the visual result is correct (see [MAUI screenshot mode](#maui-screenshot-mode)). Capture
   both tabs if both are affected.
7. Check formatting: `dotnet format src --verify-no-changes`.
8. Report per check: pass / fail (describe the failure) / uncertain.

---

## Common failure modes

| Symptom | Likely cause / where to look |
|---|---|
| New ViewModel logic has no test you can write | Logic is inside a MAUI-static-bound method — move it into `.Core` (see seam table) |
| `.Maui` project fails to build | Missing platform MAUI workload for the current OS, or you edited it on Linux where it builds as a library |
| Exporter change passes tests but the `.rep` misbehaves in-game | Validate end-to-end via the run-and-assert entry point (P0-5) / the TestHarness, not unit tests alone |
| `dotnet format` CI failure | Run `dotnet format src` and commit the result |
| Screenshot mode: PNG not written, `.error` file present | `Screenshot.Default.IsCaptureSupported` returned false, or MAUI runtime error — read the `.error` file |
| Screenshot mode: app hangs and does not exit | Increase `REPLAYSKETCH_SCREENSHOT_DELAY`; a background load may still be running during capture |
| Screenshot mode: `dotnet run` exits with build error on Linux | Expected — screenshot mode requires a MAUI runtime (maccatalyst or Windows) |

---

## Related

- Roadmap deep dive (P0): [`docs/roadmap/deep-dives/playable-baseline-harness.md`](../../docs/roadmap/deep-dives/playable-baseline-harness.md)
- Renderer visual validation: [`OpenSage.Tools.TestHarness/AGENTS.md`](../OpenSage.Tools.TestHarness/AGENTS.md)
- Architecture snapshot: [`docs/roadmap/current-state.md`](../../docs/roadmap/current-state.md)
