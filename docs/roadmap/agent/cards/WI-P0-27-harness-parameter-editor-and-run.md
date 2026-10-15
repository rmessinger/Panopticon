# Task Card `WI-P0-27`: Harness parameter editor and run command

| Field | Value |
|---|---|
| Card ID | `WI-P0-27` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-8, see [deep dive §2.8](../../deep-dives/p0-playable-baseline-harness.md#28-testharness-launcher-ui-p0-8)) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-25`, `WI-P0-26` |
| Diagrams | [P0 §8.4-8.5](../../deep-dives/p0-playable-baseline-harness.md#84-class-diagram-testharness-launcher-ui-p0-8) |

## Goal

Add `HarnessParameterEditorViewModel` (typed, validated rows for the parameters applicable to the selected scenario) and wire Run/Stop with streamed output into `HarnessLauncherViewModel`.

## Context to read (and only this)

- Deep dive §2.8 and §8.5.
- `HarnessLauncherViewModel` (`WI-P0-26`), `HarnessRunClient` and `HarnessLaunchRequest` (`WI-P0-25`).

## Steps

1. `HarnessParameterRow`: descriptor, `Text` value, `IsOverridden` (differs from default), `Error`, `Reset()`. Validation per kind: `Bool`/`Int`/`Float` parse (invariant culture), `Enum` in `EnumValues` (nullable enums also accept empty), `Json` parses, `Path` any string.
2. `HarnessParameterEditorViewModel.Load(catalog, scenario)`: rows where `AppliesTo` is empty or contains the id, excluding `Scenario`, grouped by `Group`. For `File`/`FileOrEmbedded` scenarios the `ScenarioPath` row offers `EmbeddedProfiles` as suggestions. `Headless` is locked to `true` for headless-only scenarios.
3. `BuildRequest(id)` includes only overridden rows; `HasErrors` blocks it.
4. In `HarnessLauncherViewModel`: selecting a scenario reloads the editor, keeping overrides for parameters that still apply. `RunCommand` (disabled while running or on errors) calls `HarnessRunClient`, appends lines to a bounded `Output` log (last 5000 lines) via the dispatcher, then sets `LastResult`. `StopCommand` cancels.
5. Tests: applicable-row filtering, validation per kind, override-only request, headless lock, run streaming and result, stop.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/ViewModels/HarnessParameterEditorViewModel.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/ViewModels/HarnessParameterRow.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/ViewModels/HarnessLauncherViewModel.cs`
- `test/Panopticon.Tools.ReplaySketch.Tests/HarnessParameterEditorViewModelTests.cs` (new)
- `test/Panopticon.Tools.ReplaySketch.Tests/HarnessLauncherViewModelTests.cs`

## Acceptance criteria

- [ ] Selecting `UnitValidationGround` shows `ValidationGround*` rows and hides `MassBattle*` and `EngagementCapture`.
- [ ] Entering `abc` for an `Int` row sets `Error` and disables `RunCommand`.
- [ ] A run with one edited row sends exactly one override.
- [ ] Output lines arrive in order; Stop yields `Cancelled`.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No presets; no Avalonia code.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching P0-8 item in the [implementation checklist](../../implementation-checklist.md) when its last card lands.
- Append a result line to repo-root `.exchange`.
