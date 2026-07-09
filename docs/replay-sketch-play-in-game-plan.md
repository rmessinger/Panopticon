# Plan: "Play in Game" button for ReplaySketch

> **Saved plan**: resume this in a future session by opening this file from the Copilot session state folder or referencing it with `/session`.


## Problem

Testing a replay requires:
1. Manually exporting from ReplaySketch to the Replays folder
2. Switching to the Launcher, reconfiguring it to load that replay
3. Observing issues, switching back, fixing, repeating

## Approach

Add a **"Play in Game"** button to `ExportPanel` that in one click:
1. Exports the current scenario to the game's Replays folder (same as the existing export, but with a fixed `sketch_preview.rep` name to avoid clutter)
2. Locates `OpenSage.Launcher` by walking up from ReplaySketch's own executable directory and searching sibling build output dirs
3. Spawns the launcher as a detached process with `--replay sketch_preview.rep --noaudio --noshellmap`

A new `LauncherLocatorService` handles step 2. The search strategy:
- Start from `AppContext.BaseDirectory` (ReplaySketch's own output dir)
- Walk up to the `src/` parent
- Look in `OpenSage.Launcher/bin/<config>/<tfm>/` for `OpenSage.Launcher[.exe]`
- Match the same configuration (Debug/Release) as the running tool if possible, fall back to whichever is found
- Return `null` if nothing found; the button shows a tooltip explaining the situation

## Files to change

| File | Change |
|---|---|
| `Services/LauncherLocatorService.cs` | **New**: sibling-dir search for `OpenSage.Launcher[.exe]` |
| `UI/ExportPanel.cs` | Add "Play in Game" button; inject/create `LauncherLocatorService`; wire export+launch |
| `UI/MainForm.cs` | Pass `GeneralsInstallationService` (already available) through to `ExportPanel` if needed |

> `MainForm` already owns `_installationService`. `ExportPanel` currently receives no services. The cleanest approach is to pass `MapMetadataService?` as it already does, and additionally pass a `LauncherLocatorService` instance (constructed once in `MainForm` and passed each frame, same pattern as the other panels).

## Launcher invocation details

```
OpenSage.Launcher[.exe] --replay sketch_preview.rep --noaudio --noshellmap
```

- `--replay` expects only the filename (no path), the launcher resolves it relative to the game's Replays folder
- The process is started with `Process.Start` using `UseShellExecute = false`; ReplaySketch does not wait for it to exit
- If the launcher exe is not found, the button is disabled with an `ImGui` tooltip: `"OpenSage.Launcher not found in sibling build dirs"`
- If the export fails before launch, the error is shown and the launcher is not started

## Todos

1. `launcher-locator-service`: Create `Services/LauncherLocatorService.cs` with sibling-dir search
2. `export-panel-play-button`: Add "Play in Game" button + logic to `ExportPanel.cs` (depends on 1)
3. `main-form-wire`: Thread `LauncherLocatorService` instance through `MainForm` to `ExportPanel` (depends on 1)
