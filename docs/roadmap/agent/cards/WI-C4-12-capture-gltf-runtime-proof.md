# Task Card `WI-C4-12`: Capture glTF runtime proof

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C4-12` |
| Roadmap phase | [Track C · C4](../../track-c-external-assets.md): First production external format |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C2-06`, `WI-C3-11`, `WI-C4a-10`, `WI-C4-01`, `WI-C4-02`, `WI-C4-03`, `WI-C4-04`, `WI-C4-05`, `WI-C4-06`, `WI-C4-07`, `WI-C4-08`, `WI-C4-09`, `WI-C4-10`, `WI-C4-11` |
| Diagrams | [C4 architecture diagrams](../../deep-dives/c4-gltf-importer.md#11-architecture-diagrams) |

## Goal

Import, materialize, animate, and capture the committed static PBR, rigid-node, and skinned
fixtures through the C1-C3 runtime path, completing C4 with deterministic visual proof.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4-gltf-importer.md` sections 10 and 11.
- `src/Panopticon.Tools.TestHarness/AGENTS.md`: visual-validation workflow.
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`.
- `src/Panopticon.Tools.TestHarness/Program.cs`.
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`.
- `src/Panopticon.Tools.TestHarness/StandaloneModelCapture.cs` (new from `WI-C4a-10`).
- `src/Panopticon.Simulation/Content/Loaders/ImportedModelMaterializer.cs`.
- `src/Panopticon.Simulation/Content/Loaders/ImportedAnimationMaterializer.cs`.
- `src/Panopticon.Simulation/Graphics/ModelInstance.cs`.
- `test/Panopticon.Simulation.Tests/Content/Importing/Gltf/Fixtures/pbr-materials.gltf` (new from
  `WI-C4-11`).
- `test/Panopticon.Simulation.Tests/Content/Importing/Gltf/Fixtures/pbr-materials.bin` (new from
  `WI-C4-11`).
- `test/Panopticon.Simulation.Tests/Content/Importing/Gltf/Fixtures/base-color.png` (new from `WI-C4-11`).
- `test/Panopticon.Simulation.Tests/Content/Importing/Gltf/Fixtures/emissive.jpg` (new from `WI-C4-11`).
- `test/Panopticon.Simulation.Tests/Content/Importing/Gltf/Fixtures/rigid-scene.gltf` (new from
  `WI-C4-11`).
- `test/Panopticon.Simulation.Tests/Content/Importing/Gltf/Fixtures/rigid-scene.bin` (new from
  `WI-C4-11`).
- `test/Panopticon.Simulation.Tests/Content/Importing/Gltf/Fixtures/animated-skin.glb` (new from
  `WI-C4-11`).
- `test/Panopticon.Tools.ReplaySketch.Tests/StandaloneModelCaptureTests.cs` (new from `WI-C4a-10`).

## Steps

1. Add `GltfRuntimeProof` to `BuiltInScenario` and dispatch it before ordinary replay-scenario
   construction.
2. Add a `--gltf-fixture-root` override and a matching config value. Require
   `CNC_GENERALS_PATH` only as the rendering host; require the fixture root to contain the
   committed C4 fixtures and report missing prerequisites directly.
3. Construct the internal glTF importers explicitly with default `GltfImportOptions`. Import the
   static PBR, animated rigid-node, and animated skin fixtures through a file system rooted at the
   fixture directory.
4. Materialize through `ImportedModelMaterializer` and
   `ImportedAnimationMaterializer`. Bind animation through C2 to the materialized model hierarchy;
   do not construct runtime graphics objects from SharpGLTF data.
5. Capture one static PBR frame, two rigid-node animation times, and at least three skinned
   animation times, including a time between step keys. Extend `StandaloneModelCapture` only with
   the smallest callback or fixed-time sequence seam needed to update its owned `ModelInstance`.
6. Write a validation guide covering upright +X-forward orientation, one-meter scale, winding,
   named rigid sub-object movement, PBR textures and alpha, two-influence deformation, named
   binding, inverse binds, non-identity rest pose, and step holding. Inspect every frame.
7. Add non-GPU tests for argument dispatch, missing prerequisites, selected fixture keys, and
   fixed capture times. Keep pixel and visual assertions in the TestHarness run.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`
- `src/Panopticon.Tools.TestHarness/Program.cs`
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`
- `src/Panopticon.Tools.TestHarness/StandaloneModelCapture.cs` (new from `WI-C4a-10`)
- `src/Panopticon.Tools.TestHarness/GltfRuntimeProofScenario.cs` (new)
- `test/Panopticon.Tools.ReplaySketch.Tests/GltfRuntimeProofScenarioTests.cs` (new)

## Acceptance criteria

- [ ] Static output follows `GltfModelImporter` to `ImportedModelMaterializer` to runtime `Model`.
- [ ] Animated output additionally follows C2 name binding and `AnimationInstance`.
- [ ] Captures prove core PBR, coordinate and unit conversion, rigid attachment, two-influence
      skinning, inverse binds, rest-relative animation, and step interpolation.
- [ ] Fixed rigid-node frames prove named animation moves the attached unskinned sub-object.
- [ ] Repeated runs produce the same frame set and validation-guide checks.
- [ ] No source-format branch or SharpGLTF type enters runtime graphics code.
- [ ] C4 is marked complete only after every visual check passes.
- [ ] Standard validation passes.

## Validation commands

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario GltfRuntimeProof \
  --gltf-fixture-root "$PWD/test/Panopticon.Simulation.Tests/Content/Importing/Gltf/Fixtures"
dotnet build src
dotnet test src --filter "FullyQualifiedName~GltfRuntimeProofScenarioTests|FullyQualifiedName~GltfFixtureTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add ReplaySketch UI, C5 diagnostics, a global importer registry, or a shipped-game glTF
  registration.
- Do not use third-party assets or require a separate model viewer.
- Do not change C2 or C3 acceptance expectations to accommodate a C4 conversion defect.
- Do not add C6 morph or advanced material features.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, set C4 Status to `Done` and tick C4 in the implementation checklist.
- If done and `WI-C5-06` is done, change `WI-C5-07` to `Ready`.
- Append a result line to `.exchange`, including capture paths and per-frame verdicts.
