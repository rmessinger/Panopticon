# Task Card `WI-C5-07`: Host external model requests

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C5-07` |
| Roadmap phase | [Track C · C5](../../track-c-external-assets.md): Authoring and validation tooling |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4-12`, `WI-C5-01`, `WI-C5-02`, `WI-C5-06` |
| Diagrams | [C5 architecture diagrams](../../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) |

## Goal

Add TestHarness external-model mode through request parsing, isolated import and materialization,
optional C2 binding, and complete disposal. Rendering remains in `WI-C5-08`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c5-external-model-tooling.md` sections 2, 8, and 12.
- `src/OpenSage.Tools.TestHarness/Program.cs`.
- `src/OpenSage.Tools.TestHarness/ScenarioBuilder.cs`: game installation resolution.
- `src/OpenSage.Tools.TestHarness/ITestHarnessReporter.cs`.
- `src/OpenSage.Tools.ReplaySketch.Tests/ScenarioCaptureRunnerTests.cs`: conditional graphics-test
  precedent.
- `src/OpenSage.Tools.ReplaySketch.Tests/OpenSage.Tools.ReplaySketch.Tests.csproj`.
- `src/OpenSage.Game/AssemblyInfo.cs`: TestHarness friend assembly access.
- `src/OpenSage.Game/Content/Loaders/AssetLoadContext.cs`.
- `src/OpenSage.Game/Content/Loaders/ImportedModelMaterializer.cs`.
- `src/OpenSage.Game/Content/Loaders/ImportedAnimationMaterializer.cs`.
- `src/OpenSage.Game/Graphics/Animation/ModelAnimation.cs`.
- `src/OpenSage.Game/Content/Importing/ExternalFormatFactory.cs` (new, from `WI-C5-01`).
- `src/OpenSage.Game/Content/Importing/ExternalAssetSourceMount.cs` (new, from `WI-C5-02`).
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelRequest.cs` (new, from
  `WI-C5-06`).

## Steps

1. Dispatch `--external-model-request <request.json>` before ordinary TestHarness config loading
   and scenario construction. Return a precise nonzero code for missing, duplicate, unreadable, or
   invalid request arguments.
2. Deserialize and validate the request through the Core protocol implementation.
3. Resolve a Generals installation only after request validation, then create one isolated game
   graphics host with audio and ordinary scenario startup disabled.
4. Create independent model and optional animation `ExternalAssetSourceMount` instances and
   explicit importer selections. Never combine either mount with the installation filesystem.
5. Import complete CPU graphs in their own `AssetImportContext` values. Map failures through the
   C5 diagnostic table and stop before materialization when eligibility is blocked.
6. Construct an external `AssetLoadContext` whose filesystem is the model mount and whose graphics
   device, standard resources, shaders, shader set, and asset store come from the game host.
7. Materialize the model and optional animation through the existing materializers. Bind the
   materialized animation through C2 to the model hierarchy and record binding diagnostics.
8. Add an injectable render-stage seam that `WI-C5-08` can implement. This card may return a
   structured not-rendered result after successful materialization.
9. Dispose bound animation state, runtime model, external load context owners, mounts, game, scene,
   and graphics resources in reverse ownership order for every success and failure path.
10. Link the committed C4 fixture directory into the ReplaySketch test output with
    `CopyToOutputDirectory="PreserveNewest"` and resolve it from `AppContext.BaseDirectory`.
11. Add tests for early argument failures, validation-before-installation, root isolation,
    external texture filesystem selection, static materialization, cross-document binding, and
    disposal after injected failures. Graphics-backed cases must return without asserting when no
    Generals installation or headless graphics backend is available, matching
    `ScenarioCaptureRunnerTests`.

## Files likely to change

- `src/OpenSage.Tools.TestHarness/Program.cs`
- `src/OpenSage.Tools.TestHarness/ExternalModels/ExternalModelHost.cs` (new)
- `src/OpenSage.Tools.TestHarness/ExternalModels/IExternalModelRenderStage.cs` (new)
- `src/OpenSage.Tools.TestHarness/ExternalModels/ExternalModelHostResult.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Tests/OpenSage.Tools.ReplaySketch.Tests.csproj`
- `src/OpenSage.Tools.ReplaySketch.Tests/ExternalModelHostTests.cs` (new)

## Acceptance criteria

- [ ] External-model dispatch occurs before `TestHarness.json` loading and ordinary scenarios.
- [ ] Invalid requests require neither game installation nor graphics initialization.
- [ ] Model materialization resolves C3 images only through the model source filesystem.
- [ ] Optional animations import from their own root and bind through C2 by name.
- [ ] TestHarness receives internal importer access only through the existing friend assembly.
- [ ] Every owned CPU, GPU, filesystem, game, and scene resource is disposed on success or failure.
- [ ] Graphics-backed tests skip without failure when the installation or graphics backend is
      unavailable.
- [ ] No rendered frame is required until `WI-C5-08`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~ExternalModelHostTests|FullyQualifiedName~ImportedModelMaterializerTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not render a scene, write a PNG, or add ReplaySketch process launching.
- Do not layer external roots into the installation `CompositeFileSystem` or asset store.
- Do not construct runtime models from SharpGLTF objects.
- Do not swallow disposal failures or leave process-wide graphics state changed.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4a-10` is done, change `WI-C5-08` to `Ready`.
- Append a result line to `.exchange`.
