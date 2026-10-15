# Task Card `WI-P0-25`: ReplaySketch harness run client

| Field | Value |
|---|---|
| Card ID | `WI-P0-25` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-8, see [deep dive §2.8](../../deep-dives/p0-playable-baseline-harness.md#28-testharness-launcher-ui-p0-8)) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-24` |
| Diagrams | [P0 §8.4-8.5](../../deep-dives/p0-playable-baseline-harness.md#84-class-diagram-testharness-launcher-ui-p0-8) |

## Goal

Add `HarnessLaunchRequest` and `HarnessRunClient`: write a full config from catalog defaults plus overrides to a temp file, run `TestHarness --config <file>`, stream output lines, support cancellation, and report the exit code with its catalog meaning.

## Context to read (and only this)

- Deep dive §2.8 "Launch" and §8.5.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/HarnessCatalogClient.cs` and its process seam (`WI-P0-24`).
- `src/Panopticon.Tools.ReplaySketch.Core/Services/TestHarnessPreviewClient.cs`: owned-process termination.

## Steps

1. Add `HarnessLaunchRequest` (`ScenarioId`, `Overrides` keyed by parameter name) with `ToConfigJson(HarnessCatalog)`: start from every parameter's `Default`, set `Scenario = ScenarioId`, apply overrides; throw on an override name absent from the catalog.
2. Add `HarnessRunClient.RunAsync(request, catalog, IProgress<string>, CancellationToken)`: write the JSON to a unique temp file, launch with `--config`, report each stdout and stderr line (stderr prefixed `[stderr] `), delete the temp file on exit unless `KeepConfig` is set.
3. Cancellation kills the owned process tree and returns `Cancelled`.
4. Return `HarnessRunResult` (`ExitCode`, `Meaning` from the scenario's `ExitCodes` or `"undocumented exit code"`, `Cancelled`, `ConfigPath`).
5. Tests with the fake process: config JSON content, line streaming order, exit-code meaning lookup, cancel, temp-file cleanup.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Model/HarnessLauncher/HarnessLaunchRequest.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/Model/HarnessLauncher/HarnessRunResult.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/Services/HarnessRunClient.cs` (new)
- `test/Panopticon.Tools.ReplaySketch.Tests/HarnessRunClientTests.cs` (new)

## Acceptance criteria

- [ ] Generated config contains every catalog parameter, `Scenario` set to the request id, and overrides applied.
- [ ] Unknown override name throws before any process starts.
- [ ] Exit code 5 for `ScenarioCapture` reports the catalog meaning.
- [ ] Cancellation terminates the fake process and reports `Cancelled`.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No UI threading here; the view model marshals progress via `IMainThreadDispatcher`.
- No presets.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching P0-8 item in the [implementation checklist](../../implementation-checklist.md) when its last card lands.
- Append a result line to repo-root `.exchange`.
