# Task Card `WI-P0-28`: Harness launch presets

| Field | Value |
|---|---|
| Card ID | `WI-P0-28` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-8, see [deep dive §2.8](../../deep-dives/p0-playable-baseline-harness.md#28-testharness-launcher-ui-p0-8)) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-27` |
| Diagrams | [P0 §8.4-8.5](../../deep-dives/p0-playable-baseline-harness.md#84-class-diagram-testharness-launcher-ui-p0-8) |

## Goal

Save, load, delete, and import named launch presets (scenario id + overrides) so common TestHarness configurations are one click away.

## Context to read (and only this)

- `src/Panopticon.Tools.ReplaySketch.Core/Services/PlatformPaths.cs`: app-data directory.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/ExternalModelArtifactStore.cs`: file-store pattern with an injectable root.
- `HarnessLaunchRequest` and `HarnessLauncherViewModel` from earlier P0-8 cards.
- `src/Panopticon.Tools.TestHarness/TestHarness.json`: JSONC with comments (import must tolerate comments and trailing commas).

## Steps

1. Add `HarnessPresetStore` (root injectable; default `<app data>/harness-presets/`): `List()`, `Save(name, request)`, `Load(name)`, `Delete(name)`. File name = sanitized preset name + `.json`.
2. `ImportConfig(path, catalog)`: read a `TestHarness.json`-shaped file (comments, trailing commas allowed), take `Scenario` as the id, keep only values that differ from catalog defaults as overrides, ignore unknown keys and report them.
3. View model: `Presets`, `SavePresetCommand(name)`, `LoadPresetCommand` (selects the scenario, clears filters if it is hidden, applies overrides; overrides naming parameters no longer in the catalog are dropped and listed in `StatusMessage`), `DeletePresetCommand`, `ImportConfigCommand(path)`.
4. Tests: round trip, sanitized names, import of the checked-in `TestHarness.json`, stale-parameter handling.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Services/HarnessPresetStore.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/ViewModels/HarnessLauncherViewModel.cs`
- `test/Panopticon.Tools.ReplaySketch.Tests/HarnessPresetStoreTests.cs` (new)
- `test/Panopticon.Tools.ReplaySketch.Tests/HarnessLauncherViewModelTests.cs`

## Acceptance criteria

- [ ] Save then load restores scenario and overrides exactly.
- [ ] Importing the checked-in `TestHarness.json` yields `UnitValidationGround` with `ExitAfterTargetsDestroyed = false` among the overrides.
- [ ] A preset naming a removed parameter loads the rest and reports the dropped name.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Presets store overrides only, never catalog defaults or the full config.
- No Avalonia code.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching P0-8 item in the [implementation checklist](../../implementation-checklist.md) when its last card lands.
- Append a result line to repo-root `.exchange`.
