# Task Card `WI-P9-05`: TestHarness wiring for both run modes

| Field | Value |
|---|---|
| Card ID | `WI-P9-05` |
| Roadmap phase | [Track P · P9](../../track-p-playable-baseline.md): P9-5 TestHarness wiring, see [deep dive §2.8](../../deep-dives/p9-unit-validation-ground.md#28-two-run-modes-one-definition) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P9-03`, `WI-P9-04` |
| Diagrams | [P9 Architecture Diagrams](../../deep-dives/p9-unit-validation-ground.md#6-architecture-diagrams) |

## Goal

Run the validation ground from `Panopticon.Tools.TestHarness` in both the windowed and headless loops,
driven by one scenario definition, spawn host, and timer, without disturbing the existing
replay-driven scenarios.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p9-unit-validation-ground.md`, §2.1 and §2.8, plus the "Host integration
  points" appendix entry.
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`: the enum to extend.
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`: the config surface, including `Headless`,
  `Scenario`, `GamePath`, and the camera options.
- `src/Panopticon.Tools.TestHarness/Program.cs:190-205`: the mode branch between `RunHeadless` and
  `RunWindowed`.
- `src/Panopticon.Tools.TestHarness/Program.cs:206-260`: `RunWindowed`, with `game.StartRun()` at
  `:217` and the poll block at `:253-255`.
- `src/Panopticon.Tools.TestHarness/Program.cs:262-340`: `RunHeadless`, with `game.StartRun()` at
  `:271` and the poll block at `:331-333`.
- `src/Panopticon.Tools.TestHarness/ValidationGroundHost.cs`: the host from `WI-P9-03`.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/UnitActionTimer.cs`: the timer from `WI-P9-04`.

## Steps

1. Add a `UnitValidationGround` value to `BuiltInScenario`.
2. Add the config knobs the ground needs to `TestHarnessConfig`: map path override, settle frames,
   seed, baseline path, and a baseline refresh flag. Give every knob a default so existing
   `TestHarness.json` files keep working unchanged.
3. Branch the boot path so the validation ground does not export a `.rep`: it builds a `SpawnPlan`,
   loads the configured map, and hands off to `ValidationGroundHost`. Leave the existing replay export
   and `RunReplay` path untouched for the other scenarios.
4. In `RunWindowed`, call `ValidationGroundHost.Spawn` immediately after `game.StartRun()`, advance
   the settle stage, call `IssueOrders`, and add the host and timer poll to the existing per-frame
   poll block alongside the recorder polls.
5. Mirror the same sequence in `RunHeadless`.
6. Position the camera so the whole ground is visible on entry in windowed mode, reusing the existing
   camera-zoom helper rather than adding a parallel one.
7. Ensure the run terminates: end when every unit has completed or timed out, rather than relying on
   a fixed frame cap.
8. Ensure any new `.cs` file is UTF-8 with BOM. Run `dotnet format src`, then verify
   `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`
- `src/Panopticon.Tools.TestHarness/Program.cs`

## Acceptance criteria

- [ ] `BuiltInScenario.UnitValidationGround` runs in both windowed and headless modes from the same
      scenario definition, spawn host, and timer.
- [ ] The existing `AlpineAssaultUSAvGLA` and `AlpineAssaultUSAvGLAFullBuild` scenarios still run
      unchanged through the replay path.
- [ ] The validation ground does not export a `.rep`.
- [ ] Every new config knob has a default, so an existing `TestHarness.json` needs no edit.
- [ ] The run terminates on completion or timeout of the last unit, not on a fixed frame cap.
- [ ] In windowed mode the whole ground is visible without manual camera movement.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and
      `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes

# Live run, requires a Generals install
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness
```

## Out of scope / guardrails

- Do not change the behaviour of the existing replay scenarios.
- Do not add the baseline comparison or the exit code here; that is `WI-P9-07`.
- Do not duplicate the windowed loop into a separate program; both modes share one code path by
  design.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P9-5 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
