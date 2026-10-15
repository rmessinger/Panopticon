# Task Card `WI-B5-01`: Render-path counters and render-scale probe (baseline)

| Field | Value |
|---|---|
| Card ID | `WI-B5-01` |
| Roadmap phase | [Track B · B5](../../track-b-scale.md): B5-1 baseline |
| Status | `Done` |
| Type | `pattern-replication` (counters + probe runner, modeled on existing harness code) |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | deliverable grounds the probe flow in [B5 diagrams §6](../../deep-dives/b5-rendering-at-scale.md#6-architecture-diagrams); no source-edit diagram gate applies to this measurement-only card |

## Goal

Add per-frame render counters and spans to `RenderPipeline` and a TestHarness
`RenderingScaleProbe` scenario that renders a large repeated-model scene headlessly, so B5
has a measured before/after contract. Record the baseline numbers in the deep dive.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b5-rendering-at-scale.md`: sections 1, 3.4, 4, 7.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`: `Execute`, `Render3DScene`,
  `DoRenderPass`, `RenderedObjectsOpaque`/`RenderedObjectsTransparent`.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderItemCollection.cs`: `CullAndSort`,
  `CulledItemIndices`, `Length`.
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`: headless boot, frame
  stepping, `CaptureScreenshot`, `BenchmarkReportWriter` usage (lines 60-200, 425-500, 740-770).
- `src/Panopticon.Benchmarks.Simulation/BenchmarkRunRecord.cs`,
  `BenchmarkStatistics.cs`, `BenchmarkReportWriter.cs`, `EnvironmentRecord.cs`.
- `src/Panopticon.Benchmarks.Simulation/TenThousandScaleAnchorBenchmarkScenario.cs`: anchor
  composition and `BenchmarkUnitSpawner` grid placement.
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`, `BuiltInScenario.cs`, `Program.cs`
  (config dispatch), `CameraZoomInitializer.cs`.

## Steps

1. Add `RenderStatistics` (plain counters: items added per bucket, visible items per pass,
   draw calls, instances drawn; reset at `Execute` start) and a
   `public event Action<string, TimeSpan>? RenderSpan` to `RenderPipeline`; raise spans for
   `buildRenderList`, `shadowPass`, `forwardPass` (opaque+transparent+water) and a
   `render` total around `Execute`. `DoRenderPass` returns visible count already; count
   draw calls where `DrawIndexed` is issued.
2. Add `RenderingScaleProbeRunner` to the TestHarness: headless `Game` (as in the capacity
   probe), spawn a grid of one repeated rigid definition (default
   `AmericaTankCrusader`; option `--b5-definition`), count default 10000
   (`--b5-unit-count`), disable AI and victory, fixed camera fitted over the field
   (auto-fit or explicit zoom via `CameraZoomInitializer`), N warmup frames (default 30)
   then M measured frames (default 120) of `game.Update(Array.Empty<InputMessage>())` +
   `game.Render()` + `GraphicsDevice.WaitForIdle()`, per-frame Stopwatch spans for update,
   render, gpu drain; capture one PNG at the end via the `CaptureScreenshot` pattern.
3. Emit a `BenchmarkRunRecord` (tick samples carry per-frame wall ms; system samples carry
   the `RenderSpan` spans; statistics with median/mean/p95) through `BenchmarkReportWriter`
   to a report path (default `rendering-scale-probe` directory), plus a probe summary to
   the reporter including `RenderStatistics` of the last measured frame.
4. Wire `RenderingScaleProbe` into `BuiltInScenario`, `Program` dispatch, and
   `TestHarnessConfig` (options: unit count, definition, map path default
   `maps/Twilight Flame/Twilight Flame.map`, warmup/measured frames, report directory,
   screenshot path).
5. Run the probe on this host (default Enhanced preset) at 10k units; fill the baseline
   column of the deep dive §7 table and note the dominant span in §7 prose.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`
- `src/Panopticon.Simulation/Graphics/Rendering/RenderStatistics.cs` (new)
- `src/Panopticon.Tools.TestHarness/RenderingScaleProbeRunner.cs` (new)
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`
- `src/Panopticon.Tools.TestHarness/Program.cs`
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`
- `docs/roadmap/deep-dives/b5-rendering-at-scale.md` (§7 numbers only)

## Acceptance criteria

- [x] `RenderingScaleProbe` runs headlessly to completion on this host and writes a JSON
      report, a CSV, and a PNG screenshot.
- [x] Report contains median/mean/p95 for frame wall ms, render span, and gpu drain, plus
      `RenderStatistics` (items per bucket, draws) in the summary.
- [x] Baseline numbers recorded in deep dive §7 with host/backend line (RADV offscreen,
      resolution, preset).
- [x] Standard validation passes.

## Result

Done 2026-10-12. Baseline at 10k `AmericaTankCrusader`: frame 375.92 ms median (2.66 fps),
render submit 232.76 ms, buildRenderList 77.35 ms, forwardPass 125.55 ms, 91433 draws,
91515 opaque items. Deviation from the card text: the default map moved to
`maps/deepdivide/deepdivide.map` because Twilight Flame fails terrain materialization under
the Enhanced preset with the pre-existing `LinearMipBuilder.ResizeLinear` "Linear mip source
is not contiguous" failure; recorded in deep dive §7, not worked around here. Validation:
build/format/brace green; `dotnet test src --filter Category!=Interactive` shows only the
eight known main failures.

## Validation commands

```sh
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
dotnet test src --filter 'Category!=Interactive'
CNC_GENERALS_PATH='/home/rmessinger/dev/Command and Conquer Generals' \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario RenderingScaleProbe --b5-unit-count 10000
```

## Out of scope / guardrails

- No rendering behavior change: counters and spans only, zero draw-path edits beyond
  counting increments.
- No logic changes; units idle, no attack orders.
- Do not tune anything; the baseline must be honest, including a missed budget.
- If `AmericaTankCrusader` fails to spawn or renders nothing, switch the default to another
  rigid definition (`AmericaVehicleHumvee`) and note it in §7; do not debug game logic.

## On completion

- Set Status to `Done`.
- Append a `.exchange` line.
- Follow-up cards: none new (WI-B5-02 next).
