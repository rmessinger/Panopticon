# Task Card `WI-X4-01`: Switch the ReplaySketch shell to `TabControl`

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X4-01` |
| Roadmap phase | [X4](../../track-x-fixes-tweaks.md): ReplaySketch TabControl shell |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [X4 architecture diagrams](../../implementation/track-x-x4-replaysketch-tabcontrol.md#architecture-diagrams) |

## Goal

Replace the hand-built tab strip in `MainWindow` (buttons, stacked frames, `SwitchTo*` and `SetTabActive`) with one Avalonia `TabControl`, keeping every tab's behavior, its selection side effects, and screenshot mode.

## Context to read (and only this)

- `docs/roadmap/implementation/track-x-x4-replaysketch-tabcontrol.md`.
- `src/Panopticon.Tools.ReplaySketch.Avalonia/MainWindow.cs`.
- `src/Panopticon.Tools.ReplaySketch.Avalonia/AGENTS.md`: screenshot mode and validation expectations.
- `src/Panopticon.Tools.ReplaySketch.Avalonia/Controls/SkiaCanvas.cs`: detach and reattach behavior.
- `docs/screenshots.md`: capture recipe.

## Steps

1. Replace the five `TabButton` fields, the five `Border` frames, the tab-bar `StackPanel`, and the frames `Grid` with a `TabControl` holding five `TabItem`s (headers `Tree`, `Map`, `Validation Ground`, `Model`, `Harness`; content the existing views). Keep the three-column layout and the export strip.
2. Delete `TabButton`, `SetTabActive`, and the `SwitchTo*` methods. Handle `TabControl.SelectionChanged` in one method that runs the existing side effects for the newly selected tab: `MapPreviewView.Invalidate()`, `ValidationGroundEditorView.Invalidate()`, the one-time production `ExternalModelViewModel` bind, and the one-time `HarnessView.LoadAsync()`.
3. Screenshot mode: map `REPLAYSKETCH_SCREENSHOT_TAB` to the matching `TabItem` and set `SelectedItem`. For `model`, bind `ExternalModelScreenshotFixture.Create()` before selecting, and make sure the production bind does not replace it.
4. Keep the dark look: tab strip background `#2A2A2A`, selected header text `#EEE`, unselected `#888888`, set through `TabControl`/`TabItem` properties or a local style in code. No new XAML files.
5. Update the `MainWindow` summary comment to describe the `TabControl`.
6. Run the app under Xvfb and switch every tab several times in each direction. Check that map, action tree, and validation ground render after reattach and keep their state (selected slot, zoom, edits). If a Skia view loses state or goes blank after reattach, stop and report with captures rather than working around it in this card.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Avalonia/MainWindow.cs`

## Acceptance criteria

- [ ] `MainWindow` has no tab buttons, frame visibility toggles, `SwitchTo*`, or `SetTabActive`.
- [ ] Selecting Model after Harness shows only the Model tab (the current stacked-frame bug is gone).
- [ ] Screenshot mode captures each of the five tabs; captures are saved to the shared viewer directory under unique versioned names and inspected, with the Map tab as the regression image.
- [ ] Repeated tab switching keeps map, action tree, and validation ground content and state.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Tools.ReplaySketch.Tests --filter Category!=Interactive
for tab in tree map validationground model harness; do
  DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  REPLAYSKETCH_SCREENSHOT_TAB=$tab \
  REPLAYSKETCH_SCREENSHOT_PATH="/mnt/pit2/Network Storage/temp/x4-tabcontrol-v1-$tab.png" \
    dotnet run --project src/Panopticon.Tools.ReplaySketch.Avalonia
done
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No new tabs, view changes, or Core changes.
- Keep `REPLAYSKETCH_SCREENSHOT_TAB` values unchanged.
- Never overwrite existing files in the shared viewer directory.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, tick X4-1 in `docs/roadmap/implementation-checklist.md`, set X4 `Done` in the track doc, update the README progress row, and fill "WI-X4-01 results" in the X4 plan.
- Append a result line to `.exchange`.
