# Task Card: `P0-08` — Headless run-and-assert entry point

| Field | Value |
|---|---|
| Card ID | `P0-08` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-5, see [deep dive §3.5](../../deep-dives/playable-baseline-harness.md)) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `P0-07` (assertion layer must exist), `P0-03` (faction-agnostic exporter) |

## Goal

Expose a single callable surface — one library method and one CLI entry point — that takes a
scenario/command file (JSON), runs it headlessly, evaluates its assertions, and returns exit code
0 (all passed) or 1 (any failed) plus a machine-readable JSON report. This is the capstone of P0:
after this card, agents can validate any authored command sequence end-to-end without a game window
or human in the loop.

## Context to read (and only this)

- `src/OpenSage.Tools.TestHarness/Program.cs:128-200` — the `RunHeadless(...)` method: boots a
  `Game`, loads a `.rep` file, runs the update loop, exits on enemy-CC destruction or frame timeout.
  This is the loop this card wraps.
- `src/OpenSage.Tools.TestHarness/Program.cs:280-337` — `BuildDeferredInjector(...)` and
  `DrainPendingInjections(...)`: the deferred-action injection plumbing used by the headless loop.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionEvaluator.cs` (from P0-07) — the
  evaluator to subscribe before the loop and call after.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs:44-100` — `Export(...)`:
  takes a `ReplayScenario`, produces a `.rep` file and a `deferredActions` list.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ScenarioProfileLoader.cs:13-55` — JSON loader
  for `ReplayScenario` (case-insensitive enums, embedded or disk-based).
- `docs/roadmap/deep-dives/playable-baseline-harness.md §3.5` — design intent.

## Steps

1. In `OpenSage.Tools.ReplaySketch.Core/`, add a `HarnessRunner` static class with one method:
   ```csharp
   public static AssertionReport RunAndAssert(
       ReplayScenario scenario,
       GameInstallation installation,
       string tempDirectory)
   ```
   The method:
   a. Exports the scenario to `tempDirectory/sketch_run.rep` using `ReplayExporter.Export`.
   b. Builds a `MapMetadataService` for the scenario's map path.
   c. Creates a `Game` (headless, audio off) using the installation — mirror
      `RunHeadless`'s boot sequence in `TestHarness/Program.cs:195-210`.
   d. Creates an `AssertionEvaluator(scenario.Assertions, game)` and calls `evaluator.Subscribe()`.
   e. Loads the `.rep` and runs the headless update loop (mirror `RunHeadless:210-270`).
      Terminate on: P3's `GameOutcome.Status == Decided`, frame timeout (read from
      `TestHarnessConfig.FrameTimeout` or default 18000), or all deferred actions exhausted.
   f. Returns `evaluator.Evaluate()`.

2. In `OpenSage.Tools.TestHarness/`, add a `run-and-assert` sub-command to `Program.Main` (or
   a new `--run-assert` flag):
   - Arguments: `--scenario <path.json>` and optionally `--game-path <dir>` (falls back to
     auto-detection via `GeneralsInstallationService`).
   - Loads the scenario via `ScenarioProfileLoader`.
   - Calls `HarnessRunner.RunAndAssert(...)`.
   - Writes the `AssertionReport.ToJson()` result to stdout.
   - Exits with code 0 if `report.AllPassed`, else 1.

3. Add an integration test in `OpenSage.Game.Tests` or `OpenSage.Tools.ReplaySketch.Tests` (use
   whichever already has headless game infrastructure) tagged `[Trait("Category", "Interactive")]`
   (requires game files) that:
   - Loads `alpine-assault-usa-vs-gla.json`.
   - Adds a `StateAssertion` for `player[0].money >= 0`.
   - Calls `HarnessRunner.RunAndAssert(...)`.
   - Asserts `report.AllPassed`.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs` (new).
- `src/OpenSage.Tools.TestHarness/Program.cs` — new `--run-assert` path.
- `src/OpenSage.Game.Tests/` or `src/OpenSage.Tools.ReplaySketch.Tests/` — integration test
  (tagged `Interactive`).

## Acceptance criteria

- [ ] `HarnessRunner.RunAndAssert(scenario, installation, tempDir)` returns an `AssertionReport`
      without throwing for the `alpine-assault-usa-vs-gla` scenario (no assertions → `AllPassed=true`).
- [ ] Running `dotnet run --project src/OpenSage.Tools.TestHarness -- --run-assert --scenario <path>`
      writes JSON to stdout and exits 0 on all-passed, 1 on any failure.
- [ ] A deliberately failing `StateAssertion` (e.g., `player[0].money < 0`) causes exit code 1
      and the JSON result contains the failing assertion ID.
- [ ] Standard (non-Interactive) tests still pass: `dotnet test src --filter Category!=Interactive`.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
# With game files installed:
dotnet test src --filter "FullyQualifiedName~RunAndAssert"
dotnet run --project src/OpenSage.Tools.TestHarness -- --run-assert --scenario src/OpenSage.Tools.ReplaySketch.Core/Profiles/alpine-assault-usa-vs-gla.json
```

## Out of scope / guardrails

- Do not add a GUI or MAUI interface to this entry point — it is CLI/library only.
- The `OutcomeAssertion` evaluation stubs until P3 lands; do not block on P3.
- Do not change existing TestHarness scenarios or the default `RunHeadless` path.
- If wiring into `TestHarness/Program.cs` is complex (many unrelated sub-commands), create a thin
  separate `OpenSage.Tools.ReplaySketch.Cli` project instead — but exhaust the in-place approach first.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P0-5 run-and-assert item in the [implementation checklist](../../implementation-checklist.md).
- Update `src/OpenSage.Tools.ReplaySketch.Maui/AGENTS.md`: replace the "forthcoming" note in the
  "How agents should validate" section with the actual invocation command.
- Append a result line to repo-root `.exchange`.
- Update the P0 row in `docs/roadmap/track-p-playable-baseline.md` if this card completes P0.
