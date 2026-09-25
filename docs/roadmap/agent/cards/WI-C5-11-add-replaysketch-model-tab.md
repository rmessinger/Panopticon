# Task Card `WI-C5-11`: Add ReplaySketch Model tab

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C5-11` |
| Roadmap phase | [Track C · C5](../../track-c-external-assets.md): Authoring and validation tooling |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C5-10` |
| Diagrams | [C5 architecture diagrams](../../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) |

## Goal

Add the MAUI Model tab with source pickers, summary, preview, diagnostics, command wiring, and a
deterministic fake-session screenshot path.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c5-external-model-tooling.md` section 9.
- `src/Panopticon.Tools.ReplaySketch.Maui/AGENTS.md`: screenshot-mode contract.
- `src/Panopticon.Tools.ReplaySketch.Maui/MainPage.xaml`.
- `src/Panopticon.Tools.ReplaySketch.Maui/MainPage.xaml.cs`: tab switching and screenshot mode.
- `src/Panopticon.Tools.ReplaySketch.Maui/Views/MapPreviewView.xaml`.
- `src/Panopticon.Tools.ReplaySketch.Maui/Views/MapPreviewView.xaml.cs`: platform picker guards.
- `src/Panopticon.Tools.ReplaySketch.Maui/Views/ValidationGroundEditorView.xaml`.
- `src/Panopticon.Tools.ReplaySketch.Maui/Views/ValidationGroundEditorView.xaml.cs`: file picker
  precedent.
- `src/Panopticon.Tools.ReplaySketch.Maui/Panopticon.Tools.ReplaySketch.Maui.csproj`.
- `src/Panopticon.Tools.ReplaySketch.Core/ViewModels/ExternalModelViewModel.cs` (new, from
  `WI-C5-10`).
- `src/Panopticon.Tools.ReplaySketch.Core/Services/IExternalModelFileSelector.cs` (new, from
  `WI-C5-10`).

## Steps

1. Add a `Model` tab button and content frame beside Tree, Map, and Validation Ground. Update every
   tab switch to set all four frame visibilities and button states explicitly.
2. Add `ExternalModelView` with the four deep-dive areas: source controls and clips, structural
   summary, captured preview and controls, and filterable diagnostics with selected detail.
3. Bind buttons and editable values to `ExternalModelViewModel` commands and state. Display every
   non-default import option or preview correction.
4. Add a MAUI file-selector implementation using platform-guarded `FilePicker` calls with `.gltf`
   and `.glb` guidance for model and animation selections. Treat picker cancellation as no change.
5. Load the returned PNG through a disposable stream or copied image source that does not retain a
   locked artifact file.
6. Wire real session, artifact store, TestHarness preview client, dispatcher, and file selector
   construction at the view boundary. Keep source parsing and command decisions in Core.
7. Extend screenshot mode with exact `REPLAYSKETCH_SCREENSHOT_TAB=model`. In that mode inject a
   deterministic fake session containing a catalog, two clips, one warning, one error detail,
   non-default options, eligibility, and a stable preview image without launching TestHarness.
8. Preserve existing default, Tree, Map, and Validation Ground screenshot behavior. Update the
   `AGENTS.md` environment-variable table to include `model`.
9. Build the MAUI target and capture Model and Map screenshots for visual inspection.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Maui/MainPage.xaml`
- `src/Panopticon.Tools.ReplaySketch.Maui/MainPage.xaml.cs`
- `src/Panopticon.Tools.ReplaySketch.Maui/AGENTS.md`
- `src/Panopticon.Tools.ReplaySketch.Maui/Views/ExternalModelView.xaml` (new)
- `src/Panopticon.Tools.ReplaySketch.Maui/Views/ExternalModelView.xaml.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Maui/Services/MauiExternalModelFileSelector.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Maui/Services/ExternalModelScreenshotFixture.cs` (new)

## Acceptance criteria

- [ ] The Model tab presents source, summary, preview, and diagnostics without changing replay
      scenario or map state.
- [ ] Model and animation pickers are separate and platform guarded.
- [ ] Render, reload, reset, selection, and option controls invoke Core commands.
- [ ] Preview image loading does not retain a file lock that blocks artifact replacement.
- [ ] `REPLAYSKETCH_SCREENSHOT_TAB=model` uses deterministic fake state and starts no child process.
- [ ] Tree, Map, and Validation Ground switching and screenshot selection remain intact.
- [ ] Model and Map screenshots are inspected and show no clipped tab or panel content.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExternalModelViewModelTests"
mkdir -p artifacts/replaysketch-c5
REPLAYSKETCH_SCREENSHOT_PATH="$PWD/artifacts/replaysketch-c5/model.png" \
REPLAYSKETCH_SCREENSHOT_TAB=model \
  dotnet run --project src/Panopticon.Tools.ReplaySketch.Maui
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
REPLAYSKETCH_SCREENSHOT_PATH="$PWD/artifacts/replaysketch-c5/map.png" \
REPLAYSKETCH_SCREENSHOT_TAB=map \
  dotnet run --project src/Panopticon.Tools.ReplaySketch.Maui
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not parse glTF, map diagnostics, or build process requests in MAUI code.
- Do not embed a live renderer or add a second standalone viewer.
- Do not mutate `MainViewModel` with external-model session state.
- Do not accept the Model screenshot without capturing and inspecting the Map regression image.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and cards `WI-C5-01` through `WI-C5-10` are done, change `WI-C5-12` to `Ready`.
- Append a result line to `.exchange`, including Model and Map screenshot paths and verdicts.
