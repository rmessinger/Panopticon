# Task Card: `P0-04` — Extract `IPreferencesStore` from ReplaySketch `MainViewModel`

| Field | Value |
|---|---|
| Card ID | `P0-04` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-1b, see [deep dive §3.1](../../deep-dives/playable-baseline-harness.md)) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |

## Goal

Make `MainViewModel`'s installation-path persistence unit-testable without a MAUI runtime by
extracting an `IPreferencesStore` seam from the direct `Preferences.Default` calls. This is the
first MAUI testability seam (P0-1b) and a concrete step toward closing the agent-validation gap
for the ReplaySketch MAUI layer.

## Context to read (and only this)

- `src/OpenSage.Tools.ReplaySketch.Maui/ViewModels/MainViewModel.cs:114-137` — the constructor,
  which calls `Preferences.Default.Get("cnc_generals_path", string.Empty)` (line ~131).
- `src/OpenSage.Tools.ReplaySketch.Maui/ViewModels/MainViewModel.cs:220-225` — `LoadMapFromFolder`,
  which calls `Preferences.Default.Set("cnc_generals_path", folderPath)` (line ~223) before the
  map load is attempted (the unconditional-save behaviour under test).
- `src/OpenSage.Tools.ReplaySketch.Maui/AGENTS.md` — the MAUI seam table (validation context).
- `src/OpenSage.Tools.ReplaySketch.Tests/GeneralsInstallationServiceTests.cs` — an existing test in
  the target test project to mirror for style.

## Steps

1. Add an `IPreferencesStore` interface (`string Get(string key, string defaultValue)` and
   `void Set(string key, string value)`). Place it where it is reachable from both the MAUI app
   and the non-MAUI test project — prefer `OpenSage.Tools.ReplaySketch.Core` (so
   `OpenSage.Tools.ReplaySketch.Tests` can reference it without a MAUI dependency).
2. Add a `MemoryPreferencesStore` (dictionary-backed) in `.Core` for tests, and a
   `MauiPreferencesStore` adapter in `.Maui` that forwards to `Preferences.Default`.
3. Give `MainViewModel` a constructor parameter `IPreferencesStore` (default to the MAUI adapter
   for production call sites). Replace the two `Preferences.Default.*` calls with the injected
   store. Do not change any other behaviour or ordering.
4. Add a unit test in `OpenSage.Tools.ReplaySketch.Tests` that constructs `MainViewModel` with a
   `MemoryPreferencesStore`, calls `LoadMapFromFolder` with a path where map loading will fail
   (no game files), and asserts the preference was saved regardless of the map-load outcome.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/` — new `IPreferencesStore` + `MemoryPreferencesStore`.
- `src/OpenSage.Tools.ReplaySketch.Maui/ViewModels/MainViewModel.cs` — constructor injection.
- `src/OpenSage.Tools.ReplaySketch.Maui/` — new `MauiPreferencesStore` adapter.
- `src/OpenSage.Tools.ReplaySketch.Tests/` — new unit test.

## Acceptance criteria

- [ ] `MainViewModel` accepts an `IPreferencesStore` via constructor injection; production uses the
      `Preferences.Default` adapter; no direct `Preferences.Default` call remains in `MainViewModel`.
- [ ] A unit test in `OpenSage.Tools.ReplaySketch.Tests` calls `LoadMapFromFolder` with a valid
      directory path where map loading fails, and asserts the preference was saved anyway.
- [ ] The test runs headlessly (no MAUI runtime) under `dotnet test src/OpenSage.Tools.ReplaySketch.Tests`.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src/OpenSage.Tools.ReplaySketch.Tests
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not abstract `MainThread` or `Process.Start` here — that is the follow-up card P0-1c.
- Do not change the command model, exporter, or any rendering/controller logic.
- Keep `MainViewModel`'s ordering and behaviour identical apart from the injected store.
- If the change pulls in unrelated files, mark `Blocked` and report.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching P0-1b item in the [implementation checklist](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - A card for P0-1c (inject a `MainThread` dispatcher seam + a process-launcher seam so
    `ValidateCommand`/`ExportCommand`/`PlayInGameCommand` and the background-load apply-results are
    unit-testable headlessly).
