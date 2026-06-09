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

## What the tests cover

`OpenSage.Tools.ReplaySketch.Tests` (SkiaSharp + xUnit, non-MAUI):

| Test file | Covers |
|---|---|
| `ActionTreeControllerTests` | Action-tree interaction logic (selection, drag, node layout) |
| `ActionTreeRendererTests` | Action-tree rendering, compared to golden PNGs via `GoldenImageHelper` |
| `MapPreviewRendererTests` | Map-preview rendering, compared to golden PNGs |
| `GeneralsInstallationServiceTests` | Installation discovery/path service |

`OpenSage.Game.Tests/Tools/ReplaySketch/ReplaySketchTests.cs` covers the command model and the
exporter (scenario → engine `Order` → `.rep`).

### Golden-image workflow

Renderer changes are validated against checked-in PNG baselines in
`OpenSage.Tools.ReplaySketch.Tests/GoldenImages/`. `GoldenImageHelper.AssertMatchesGolden`
decodes both images and allows up to a 1% pixel-mismatch ratio (tolerance 4 per channel) to
absorb anti-aliasing noise.

To intentionally update baselines after a deliberate visual change:

```bash
UPDATE_GOLDENS=1 dotnet test src/OpenSage.Tools.ReplaySketch.Tests
```

This rewrites the PNGs in both the test output directory and the project source tree so they can
be committed. Review the regenerated images before committing — never run `UPDATE_GOLDENS=1` to
mask an unexpected regression.

---

## The MAUI layer: what is and is not validatable

`MainViewModel` (`ViewModels/MainViewModel.cs`) currently binds directly to MAUI statics, which
is why its command flows are not yet headlessly testable:

| Binding | Where | Roadmap seam to remove it |
|---|---|---|
| `Preferences.Default` | constructor + `LoadMapFromFolder` | P0-1b: extract `IPreferencesStore` |
| `MainThread.BeginInvokeOnMainThread` | `StartBackgroundLoad` apply-results | P0-1c: inject a dispatcher seam |
| `Process.Start` / launcher | `ExecutePlayInGame` | P0-1c: inject a process-launcher seam |

Until those seams land, do not add new logic directly inside `MainViewModel` methods that touch
these statics — you will create untestable code. Put the logic in a `.Core` service and have the
ViewModel call it. When you do touch a flow that has a seam, add a unit test in
`OpenSage.Tools.ReplaySketch.Tests` using the in-memory/synchronous test double.

Forthcoming (P0-4/P0-5): a headless run-and-assert entry point — scenario/command file in,
machine-readable pass/fail report out (exit code + JSON) — for command-level validation. Once it
lands, prefer it for validating exporter/command changes end-to-end. Status:
[playable-baseline-harness.md](../../docs/roadmap/deep-dives/playable-baseline-harness.md).

---

## How agents should validate a ReplaySketch change

1. Identify where your change lives (`.Core` vs `.Maui`) using the split table above.
2. Build: `dotnet build src`.
3. Run the headless suites:
   `dotnet test src/OpenSage.Tools.ReplaySketch.Tests` and the `ReplaySketch` filter on
   `OpenSage.Game.Tests`.
4. If you changed a renderer, inspect any golden-image diff. If the change is intentional,
   regenerate with `UPDATE_GOLDENS=1` and visually confirm the new PNGs before committing.
5. If you changed `.Core` behaviour, add or extend a headless test that asserts it.
6. If you changed exporter/command behaviour, validate the produced `.rep` / orders via the
   command-model tests (and the run-and-assert entry point once available).
7. Check formatting: `dotnet format src --verify-no-changes`.
8. Report per check: pass / fail (describe the failure) / uncertain.

---

## Common failure modes

| Symptom | Likely cause / where to look |
|---|---|
| Golden-image test fails after an unrelated change | Anti-aliasing drift or a real render regression — inspect the diff; do not blind-regenerate |
| New ViewModel logic has no test you can write | Logic is inside a MAUI-static-bound method — move it into `.Core` (see seam table) |
| `.Maui` project fails to build | Missing platform MAUI workload for the current OS, or you edited it on Linux where it builds as a library |
| Exporter change passes tests but the `.rep` misbehaves in-game | Validate end-to-end via the run-and-assert entry point (P0-5) / the TestHarness, not unit tests alone |
| `dotnet format` CI failure | Run `dotnet format src` and commit the result |

---

## Related

- Roadmap deep dive (P0): [`docs/roadmap/deep-dives/playable-baseline-harness.md`](../../docs/roadmap/deep-dives/playable-baseline-harness.md)
- Renderer visual validation: [`OpenSage.Tools.TestHarness/AGENTS.md`](../OpenSage.Tools.TestHarness/AGENTS.md)
- Architecture snapshot: [`docs/roadmap/current-state.md`](../../docs/roadmap/current-state.md)
