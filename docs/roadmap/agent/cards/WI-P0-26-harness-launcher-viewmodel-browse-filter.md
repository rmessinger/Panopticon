# Task Card `WI-P0-26`: Harness launcher view model: browse and filter

| Field | Value |
|---|---|
| Card ID | `WI-P0-26` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-8, see [deep dive §2.8](../../deep-dives/p0-playable-baseline-harness.md#28-testharness-launcher-ui-p0-8)) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-24` |
| Diagrams | [P0 §8.4-8.5](../../deep-dives/p0-playable-baseline-harness.md#84-class-diagram-testharness-launcher-ui-p0-8) |

## Goal

Add `HarnessLauncherViewModel` in ReplaySketch.Core that loads the catalog and exposes filtered, selectable scenarios (text, category, mode).

## Context to read (and only this)

- `src/Panopticon.Tools.ReplaySketch.Core/ViewModels/ExternalModelViewModel.cs`: Core view-model pattern (notifications, `AsyncCommand`, dispatcher injection).
- `src/Panopticon.Tools.ReplaySketch.Core/ViewModels/AsyncCommand.cs`.
- `test/Panopticon.Tools.ReplaySketch.Tests/ExternalModelViewModelTests.cs`: test pattern.

## Steps

1. Add the view model with injected `HarnessCatalogClient` (or an interface over it) and `IMainThreadDispatcher`.
2. `LoadCatalogCommand` populates `AllScenarios`; failures set `StatusMessage` with the error code.
3. Filters: `FilterText` (case-insensitive match on id or summary), `CategoryFilter` (empty = all), `ModeFilter` (scenarios whose `Modes` include it; `None` = all). `VisibleScenarios` recomputes on any filter change and keeps a stable order (category, then id).
4. `SelectedScenario` clears when filtered out.
5. Tests: load success and failure, each filter alone and combined, selection clearing.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/ViewModels/HarnessLauncherViewModel.cs` (new)
- `test/Panopticon.Tools.ReplaySketch.Tests/HarnessLauncherViewModelTests.cs` (new)

## Acceptance criteria

- [ ] Filtering `ee2` shows only the `Ee2*` proof scenarios.
- [ ] `ModeFilter = Windowed` hides headless-only scenarios.
- [ ] A load failure surfaces its `HRN` code in `StatusMessage` and leaves `VisibleScenarios` empty.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Parameter editing and running (`WI-P0-27`), presets (`WI-P0-28`), and Avalonia code (`WI-P0-29`) are separate cards.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching P0-8 item in the [implementation checklist](../../implementation-checklist.md) when its last card lands.
- Append a result line to repo-root `.exchange`.
