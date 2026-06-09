# Implementation Plan: Track P · P0-5 — Run-and-Assert Entry Point (+ end-to-end test)

> Track: [Track P — Playable Baseline](../track-p-playable-baseline.md)
> Phase: [P0 — ReplaySketch arbitrary-command harness](../deep-dives/playable-baseline-harness.md) · sub-phase P0-5: Run-and-assert entry point
> Checklist items: [implementation-checklist.md](../implementation-checklist.md) — P0 items "Provide a headless run-and-assert entry point cheap agents can call" and "Tests: author + validate an arbitrary command set end-to-end"
> Depends on: [track-p-p0-5-assertion-layer.md](track-p-p0-5-assertion-layer.md) (the evaluator/report this wraps).

## Problem

The headless run loop today lives inside `TestHarness/Program.cs` (`RunHeadless`, `:195-271`) as a
bespoke, window-tool-coupled routine that exits on enemy-CC destruction and has no return value. A
cheap agent or CI job has no single callable surface that takes a scenario + commands + assertions,
runs them headlessly with no renderer/window, and returns a machine-readable pass/fail result. P0's
whole point — "an arbitrary, procedurally-generated order set … runs headlessly and is asserted
pass/fail" (the P0 milestone) — needs exactly that entry point.

Checklist item ACs:
- One CLI/library call takes a scenario/command file and returns exit code 0/non-zero + a JSON
  report; documented invocation; runnable headlessly in CI.
- An automated test authors a multi-command scenario, runs it headlessly, and asserts
  post-conditions; passes under `dotnet test src --filter Category!=Interactive`.

## Design

A single library entry point with a thin CLI wrapper, reusing the existing headless boot/loop but
decoupled from the windowed `TestHarness`.

### Library surface (the callable unit)

- `HeadlessRunner.RunAndAssert(ReplayScenario scenario, MapMetadataService map, AssertionSet assertions, RunOptions options) -> AssertionReport`
  (Core or a new `OpenSage.Tools.ReplaySketch.Core` service), which:
  1. exports the scenario to a temp `.rep` + deferred actions via `ReplayExporter.Export`;
  2. boots a headless `Game` with `Configuration { NoAudio = true }` and no window — extract the
     boot/loop from `TestHarness/Program.cs:195-268` into a reusable, renderer-optional runner
     (render/capture calls become optional; CI runs with them off);
  3. installs the deferred injector + object-reference resolution
     ([P0-3](track-p-p0-3-runtime-object-references.md)) and the `AssertionEvaluator`
     ([P0-4](track-p-p0-5-assertion-layer.md)) on `Game.Updating`;
  4. terminates on the real outcome signal (P3 `GameOutcome` when available; until then, on the
     existing enemy-CC-destroyed runout or a frame timeout — `:209-211,:256-267`);
  5. returns the `AssertionReport`.
- `RunOptions`: `maxFrames`, `render` (default false for CI), `captureDir?`, `seed?`.

### Decoupling from the window tool

- The current loop calls `game.Panel.EnsureFrame`/`game.Render`/`capture.FlushPending`
  (`:250-252`) and depends on a swapchain. Make rendering optional so the runner ticks
  `game.Update(...)` headlessly with no GPU present (matching the existing headless tests'
  expectations). Keep the windowed `TestHarness.RunHeadless` as a thin caller of the shared runner
  with `render = true`, so the visual-validation workflow (AGENTS.md) is unchanged.

### CLI wrapper

- A small command (extend the existing `TestHarness` CLI, or a `replaysketch run` verb) taking
  `--scenario <file>` (scenario JSON incl. embedded assertions), optional `--map`, `--out <report.json>`,
  `--max-frames`, `--no-render`. It loads the scenario, calls `RunAndAssert`, writes the JSON report,
  and sets process exit code 0 (all assertions pass) or non-zero (any fail / run error). Document the
  invocation in the [P0 deep dive](../deep-dives/playable-baseline-harness.md) and the
  [agent-guide](../agent-guide/README.md) so cheap agents can call it.

### End-to-end test (the second checklist item)

- A test in `OpenSage.Game.Tests` (extends `MockedGameTest`/headless boot, `Category!=Interactive`)
  that programmatically authors a multi-command scenario (e.g. build → produce → move → attack),
  attaches state + invariant assertions (money never negative; a unit was produced), calls
  `RunAndAssert`, and asserts the returned report is all-pass. This is the standing "harness works"
  regression and the template P7 later scales into a full-match check.

## Files to change

| File | Change |
|---|---|
| `src/OpenSage.Tools.ReplaySketch.Core/Services/HeadlessRunner.cs` | NEW — `RunAndAssert` library entry point (renderer-optional boot/loop) |
| `src/OpenSage.Tools.ReplaySketch.Core/Services/RunOptions.cs` | NEW — run options |
| `src/OpenSage.Tools.TestHarness/Program.cs` | Refactor `RunHeadless` to call the shared runner with `render = true`; add the CLI `run` verb (or a sibling CLI) |
| `docs/roadmap/deep-dives/playable-baseline-harness.md` | Document the CLI invocation + exit-code/report contract |
| `docs/roadmap/agent-guide/README.md` | Note the run-and-assert command as the harness entry point for cheap agents |
| `src/OpenSage.Game.Tests/Tools/ReplaySketch/ReplaySketchEndToEndTests.cs` | NEW — the end-to-end authored-scenario test |

## Tests

- Library: `RunAndAssert` on a tiny scenario with a passing assertion returns an all-pass report and
  the wrapper maps it to exit code 0; a failing assertion maps to non-zero.
- CLI: invoking the verb on a scenario file writes a JSON report file and returns the right exit code
  (smoke test via the library surface; full process-spawn optional).
- End-to-end: the multi-command authored-scenario test above passes under
  `dotnet test src --filter Category!=Interactive`.

## Out of scope

- The assertion model/evaluator itself → [track-p-p0-5-assertion-layer.md](track-p-p0-5-assertion-layer.md).
- Real win/lose termination semantics → P3 ([victory-defeat.md](../deep-dives/victory-defeat.md));
  this wires the outcome signal in once it exists.
- Large/procedural authoring throughput → [track-p-p0-7-procedural-authoring.md](track-p-p0-7-procedural-authoring.md).
- Replay-viewport/visualization scaling → A5 ([visualization.md](../deep-dives/visualization.md)).
