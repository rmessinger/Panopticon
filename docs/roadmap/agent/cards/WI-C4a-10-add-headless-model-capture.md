# Task Card `WI-C4a-10`: Add headless runtime-model capture

| Field | Value |
|---|---|
| Card ID | `WI-C4a-10` |
| Roadmap phase | [Track C · C4a](../../track-c-external-assets.md): shared C3/C4a runtime-model capture infrastructure |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C1-11` |
| Diagrams | [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) |

## Goal

Add a TestHarness helper that renders one already-materialized runtime `Model` to a PNG without
requiring the ImGui-based diagnostic `ModelView`. This infrastructure card may execute after
`WI-C1-11` independently of the remaining C4a cards.

## Context to read (and only this)

- `src/Panopticon.Simulation/Diagnostics/AssetViews/ModelView.cs`: model-instance update, bounds, world
  matrix, and render-list callback.
- `src/Panopticon.Simulation/Diagnostics/AssetViews/RenderedView.cs`: diagnostic scene, camera, render
  target, and render-pipeline setup.
- `src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs`: headless render execution and
  `FrameCapturer` output.
- `test/Panopticon.Tools.ReplaySketch.Tests/ScenarioCaptureRunnerTests.cs`: TestHarness internal-test
  pattern.

## Steps

1. Add an internal `StandaloneModelCapture` helper that accepts a runtime `Model`, asset-load
   context, graphics device, output path, and viewport size.
2. Create a diagnostic `Scene3D` with a fixed viewport, default world lighting, and an
   `ArcballCameraController` fitted to merged sub-object bounds. Do not construct
   `DiagnosticViewContext`, `RenderedView`, or ImGui.
3. Create and update one `ModelInstance`. Hook `RenderPipeline.BuildingRenderList` with the same
   world-matrix and `BuildRenderList` calls used by `ModelView`.
4. Execute the pipeline into a `RenderTarget` and save it through `FrameCapturer`.
5. Extract pure bounds/camera-fit calculation from the helper and add tests for empty, centered,
   and offset bounds without requiring a GPU.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/StandaloneModelCapture.cs` (new)
- `test/Panopticon.Tools.ReplaySketch.Tests/StandaloneModelCaptureTests.cs` (new)

## Acceptance criteria

- [ ] The helper has no ImGui or window dependency.
- [ ] Model placement and camera fitting follow `ModelView`.
- [ ] Empty bounds fail explicitly before renderer allocation.
- [ ] The helper writes only the caller-specified PNG.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~StandaloneModelCaptureTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add a dedicated viewer, built-in scenario, NIF reference, or installed-asset logic.
- Do not modify `ModelView` behavior.
- Do not add GUI controls or ReplaySketch UI.

## On completion

- Set Status above to `Done` or `Blocked`.
- If `WI-C1-14` and `WI-C4a-09` are done, change `WI-C4a-11` to `Ready`.
- If `WI-C3-10` is done, change `WI-C3-11` to `Ready`.
- If `WI-C4-11` is done, change `WI-C4-12` to `Ready`.
- If `WI-C5-07` is done, change `WI-C5-08` to `Ready`.
- Append a result line to `.exchange`.
