# Task Card `WI-P0-29`: ReplaySketch Harness tab (Avalonia)

| Field | Value |
|---|---|
| Card ID | `WI-P0-29` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-8, see [deep dive §2.8](../../deep-dives/p0-playable-baseline-harness.md#28-testharness-launcher-ui-p0-8)) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-28` |
| Diagrams | [P0 §8.4-8.5](../../deep-dives/p0-playable-baseline-harness.md#84-class-diagram-testharness-launcher-ui-p0-8) |

## Goal

Add a `Harness` tab to ReplaySketch.Avalonia bound to `HarnessLauncherViewModel`: filter bar, scenario list, grouped parameter editor, presets, Run/Stop, and output log; verified via screenshot mode.

## Context to read (and only this)

- `src/Panopticon.Tools.ReplaySketch.Avalonia/AGENTS.md`: thin-shell rules and screenshot validation.
- `src/Panopticon.Tools.ReplaySketch.Avalonia/MainWindow.cs`: tab buttons, `SwitchTo*`, `REPLAYSKETCH_SCREENSHOT_TAB` handling.
- `src/Panopticon.Tools.ReplaySketch.Avalonia/Views/ExternalModelView.cs`: code-built view bound to a Core view model.
- `docs/screenshots.md`: capture and sharing recipe.

## Steps

1. Add `Views/HarnessView.cs`. Left pane: filter text box, category toggles, mode combo, scenario list (id, summary, mode badges). Right pane: grouped parameter rows (check box for `Bool`, combo for `Enum`, text box otherwise, editable combo with embedded profiles for `ScenarioPath`, browse button for `Path`, multi-line box for `Json`; overridden rows highlighted, per-row reset, inline error). Bottom: presets bar, Run/Stop, result line, scrolling monospace output.
2. Add the `Harness` tab button and `SwitchToHarness` in `MainWindow`; load the catalog on first activation.
3. Accept `REPLAYSKETCH_SCREENSHOT_TAB=harness`; update the doc comment listing valid tabs.
4. Keep the view to binding and file-dialog calls only.
5. Document the tab in `ReplaySketch.Avalonia/AGENTS.md` and add the `harness` screenshot tab to `docs/screenshots.md`.
6. Capture a screenshot with `UnitValidationGround` selected and confirm the Map tab still renders.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Avalonia/Views/HarnessView.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Avalonia/MainWindow.cs`
- `src/Panopticon.Tools.ReplaySketch.Avalonia/AGENTS.md`
- `docs/screenshots.md`

## Acceptance criteria

- [ ] Screenshot of the `harness` tab shows the scenario list and parameter groups populated from the live catalog.
- [ ] Map tab screenshot is unchanged in layout (no regression).
- [ ] Launching `PbrMaterialProof` headless from the tab streams output and reports exit code 0 with its meaning.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 REPLAYSKETCH_SCREENSHOT_TAB=harness \
  REPLAYSKETCH_SCREENSHOT_PATH="/mnt/pit2/Network Storage/temp/harness-tab.png" \
  dotnet run --project src/Panopticon.Tools.ReplaySketch.Avalonia
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 REPLAYSKETCH_SCREENSHOT_TAB=map \
  REPLAYSKETCH_SCREENSHOT_PATH=/tmp/map-tab.png \
  dotnet run --project src/Panopticon.Tools.ReplaySketch.Avalonia
```

## Out of scope / guardrails

- Do not move logic into the view; missing behavior goes back into the Core view model with a test.
- Do not restyle existing tabs.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching P0-8 item in the [implementation checklist](../../implementation-checklist.md) when its last card lands.
- Set the P0-8 sub-phase Status to `Done` in the [deep dive](../../deep-dives/p0-playable-baseline-harness.md#4-sub-phases).
- Append a result line to repo-root `.exchange`.
