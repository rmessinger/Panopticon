# Task Card `WI-C5-08`: Render external model scenes

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C5-08` |
| Roadmap phase | [Track C · C5](../../track-c-external-assets.md): Authoring and validation tooling |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4-12`, `WI-C4a-10`, `WI-C5-07` |
| Diagrams | [C5 architecture diagrams](../../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) |

## Goal

Render deterministic external-model scenes at a fixed animation sample and atomically produce PNG
and report artifacts, while supporting report-only execution.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c5-external-model-tooling.md` sections 7, 8, and 10.
- `src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs`: `FrameCapturer` usage.
- `test/Panopticon.Tools.ReplaySketch.Tests/ScenarioCaptureRunnerTests.cs`: conditional graphics-test
  precedent.
- `src/Panopticon.Tools.TestHarness/StandaloneModelCapture.cs` (new, from `WI-C4a-10`).
- `src/Panopticon.Simulation/Graphics/ModelInstance.cs`.
- `src/Panopticon.Simulation/Graphics/Animation/AnimationInstance.cs`.
- `src/Panopticon.Simulation/Graphics/Animation/ModelAnimation.cs`.
- `src/Panopticon.Tools.TestHarness/ExternalModels/ExternalModelHost.cs` (new, from `WI-C5-07`).
- `src/Panopticon.Tools.TestHarness/ExternalModels/IExternalModelRenderStage.cs` (new, from
  `WI-C5-07`).
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelReport.cs` (new, from
  `WI-C5-03`).

## Steps

1. Implement the host render stage by extending `StandaloneModelCapture` only with the explicit
   model transform, camera, lighting, viewport, and fixed animation-sample inputs C5 needs.
2. Build one deterministic scene with no simulation clock dependence, random input, HUD, audio,
   game AI, or map mutation.
3. Apply yaw, pitch, roll, translation, background, lighting, and camera controls after import.
   Do not modify model vertices, skeleton data, importer basis conversion, or game definitions.
4. Bind the selected animation, evaluate exactly the requested sample time, update the
   `ModelInstance`, and render one frame. Do not implement real-time playback.
5. In report-only mode, complete import, validation, materialization, and optional binding without
   allocating a render target or writing an image.
6. Write image and report to sibling staging files, flush and close them, then atomically replace
   each final caller path. Publish the success report last; remove a newly published image if the
   report replacement fails.
7. Include sorted diagnostics, eligibility, normalized source hashes, options, pairing, render
   metadata, and optional PNG hash in the report. Omit staging and installation paths.
8. Preserve a failure report when rendering or image publication fails, with no partial final PNG.
9. Add tests for deterministic scene inputs, fixed-time animation sampling, report-only behavior,
   atomic success, injected image and report write failures, hashes, and cleanup. Graphics-backed
   cases must return without asserting when no Generals installation or headless graphics backend
   is available.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/StandaloneModelCapture.cs` (new, from `WI-C4a-10`)
- `src/Panopticon.Tools.TestHarness/ExternalModels/ExternalModelSceneRenderer.cs` (new)
- `src/Panopticon.Tools.TestHarness/ExternalModels/ExternalModelArtifactWriter.cs` (new)
- `src/Panopticon.Tools.TestHarness/ExternalModels/ExternalModelHost.cs` (new, from `WI-C5-07`)
- `test/Panopticon.Tools.ReplaySketch.Tests/ExternalModelRenderingTests.cs` (new)

## Acceptance criteria

- [ ] Repeated identical requests produce the same scene inputs, report ordering, and PNG hash.
- [ ] Animation output reflects exactly the requested sample time.
- [ ] Preview corrections affect only scene state and are recorded in the report.
- [ ] Report-only mode performs materialization and binding without creating a render target.
- [ ] Each artifact is atomically replaced; success leaves both PNG and report, while failures
      leave no partial final PNG.
- [ ] Failure reports remain inspectable and contain no staging or installation paths.
- [ ] All render resources are disposed before external-model mode exits.
- [ ] Graphics-backed tests skip without failure when the installation or graphics backend is
      unavailable.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~ExternalModelRenderingTests|FullyQualifiedName~StandaloneModelCaptureTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add an embedded live renderer, playback timer, model editor, or source conversion.
- Do not use a map scene or change deterministic camera inputs during capture.
- Do not publish partial output files or delete caller-owned directories.
- Do not add C6 rendering behavior when its feature gate is disabled.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C5-09` to `Ready`.
- Append a result line to `.exchange`, including artifact paths and deterministic hash evidence.
