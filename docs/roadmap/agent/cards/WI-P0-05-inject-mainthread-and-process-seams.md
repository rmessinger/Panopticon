# Task Card `WI-P0-05`: Inject `IMainThreadDispatcher` and `IProcessLauncher` seams into `MainViewModel`

| Field | Value |
|---|---|
| Card ID | `WI-P0-05` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-1c, see [deep dive §3.1](../../deep-dives/p0-playable-baseline-harness.md)) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-P0-04` (IPreferencesStore seam, so constructor injection pattern is already established) |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/p0-playable-baseline-harness.md#8-architecture-diagrams) |

## Goal

Abstract the two remaining MAUI statics in `MainViewModel`: `MainThread.InvokeOnMainThreadAsync`
(background map-discovery result application) and `System.Diagnostics.Process.Start`
(launch-in-game): behind small injectable interfaces, so `PlayInGameCommand` and the
background-load apply-results path are unit-testable headlessly in
`OpenSage.Tools.ReplaySketch.Tests` without a MAUI runtime.

## Context to read (and only this)

- `src/OpenSage.Tools.ReplaySketch.Maui/ViewModels/MainViewModel.cs:324-365`: `StartBackgroundMapDiscovery`:
  the `await MainThread.InvokeOnMainThreadAsync(() => { … })` lambda at line ~347 is the apply-results
  path to abstract.
- `src/OpenSage.Tools.ReplaySketch.Maui/ViewModels/MainViewModel.cs:263-286`: `ExecutePlayInGame`:
  `System.Diagnostics.Process.Start(psi)` at line ~285 is the launcher call to abstract.
- `src/OpenSage.Tools.ReplaySketch.Core/`: place the new interfaces and test doubles here so
  `OpenSage.Tools.ReplaySketch.Tests` can reference them without a MAUI dependency.
- `src/OpenSage.Tools.ReplaySketch.Tests/GeneralsInstallationServiceTests.cs`: style reference
  for tests in the target project.
- `src/OpenSage.Tools.ReplaySketch.Maui/AGENTS.md` (seam table): validation context.

## Steps

1. In `OpenSage.Tools.ReplaySketch.Core`, add:
   - `IMainThreadDispatcher` with one method: `Task InvokeOnMainThreadAsync(Action action)`.
   - `IProcessLauncher` with one method: `void Start(System.Diagnostics.ProcessStartInfo psi)`.

2. In `OpenSage.Tools.ReplaySketch.Core`, add test doubles:
   - `SynchronousMainThreadDispatcher`: runs the action synchronously inline
     (`action(); return Task.CompletedTask`).
   - `RecordingProcessLauncher`: records each `ProcessStartInfo` in a `List<ProcessStartInfo>
     Invocations`.

3. In `OpenSage.Tools.ReplaySketch.Maui`, add production adapters:
   - `MauiMainThreadDispatcher`: delegates to `MainThread.InvokeOnMainThreadAsync(action)`.
   - `SystemProcessLauncher`: calls `System.Diagnostics.Process.Start(psi)`.

4. Add `IMainThreadDispatcher` and `IProcessLauncher` constructor parameters to `MainViewModel`
   (default to the MAUI adapters for production). Replace the two static call sites with the
   injected seams. Do not change any other behaviour or ordering.

5. Add two unit tests in `OpenSage.Tools.ReplaySketch.Tests`:
   - Construct `MainViewModel` with `SynchronousMainThreadDispatcher` and a `RecordingProcessLauncher`
     (plus the `MemoryPreferencesStore` from WI-P0-04 if that card is done, or a stub if not). Call
     `StartBackgroundMapDiscovery` (or trigger the code path that sets `AvailableMaps`) synchronously
     and assert `AvailableMaps` is populated without a `MainThread` runtime.
   - Call `ExecutePlayInGame` (after setting `_map` to a non-null stub and configuring
     `_launcherLocator` to return a valid path) and assert `RecordingProcessLauncher.Invocations`
     contains exactly one entry with the expected `FileName` and `Arguments` pattern.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/`: new `IMainThreadDispatcher`, `IProcessLauncher`,
  `SynchronousMainThreadDispatcher`, `RecordingProcessLauncher`.
- `src/OpenSage.Tools.ReplaySketch.Maui/ViewModels/MainViewModel.cs`: constructor injection.
- `src/OpenSage.Tools.ReplaySketch.Maui/`: new `MauiMainThreadDispatcher`, `SystemProcessLauncher`.
- `src/OpenSage.Tools.ReplaySketch.Tests/`: two new unit tests.

## Acceptance criteria

- [ ] `MainViewModel` accepts `IMainThreadDispatcher` and `IProcessLauncher` via constructor
      injection; no direct `MainThread.*` or `System.Diagnostics.Process.Start` call remains in
      `MainViewModel`.
- [ ] `StartBackgroundMapDiscovery` apply-results path runs headlessly (synchronously, no MAUI
      runtime) in a unit test, and the test asserts `AvailableMaps` is populated.
- [ ] `ExecutePlayInGame` runs headlessly in a unit test, and `RecordingProcessLauncher.Invocations`
      contains the expected launch entry.
- [ ] Both tests run under `dotnet test src/OpenSage.Tools.ReplaySketch.Tests` with no MAUI runtime.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).
- [ ] Run screenshot mode and confirm the app still launches and renders the Tree and Map tabs
      correctly:
      ```sh
      REPLAYSKETCH_SCREENSHOT_PATH=/tmp/rs_p005_tree.png dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
      REPLAYSKETCH_SCREENSHOT_PATH=/tmp/rs_p005_map.png REPLAYSKETCH_SCREENSHOT_TAB=map dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
      ```

## Validation commands

```sh
dotnet build src
dotnet test src/OpenSage.Tools.ReplaySketch.Tests
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/rs_p005_tree.png dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
```

## Out of scope / guardrails

- Do not abstract `ValidateCommand` or `ExportCommand` internals here: only the two statics named above.
- Do not change the command model, exporter, or assertion layer.
- Keep `MainViewModel`'s observable behaviour (property-changed events, command states) identical.
- If `WI-P0-04` is not yet done, stub `IPreferencesStore` with a no-op implementation for this card's tests.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P0-1c item in the [implementation checklist](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- No further MAUI seam cards planned; P0-1b/P0-1c together complete the ViewModel testability goal.
