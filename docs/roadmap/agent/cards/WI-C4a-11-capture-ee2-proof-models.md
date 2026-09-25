# Task Card `WI-C4a-11`: Capture the EE2 proof models

| Field | Value |
|---|---|
| Card ID | `WI-C4a-11` |
| Roadmap phase | [Track C · C4a](../../track-c-external-assets.md): Empire Earth II NIF 10.1 proof of concept |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C1-14`, `WI-C4a-09`, `WI-C4a-10` |
| Diagrams | [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) |

## Goal

Materialize and capture both imported EE2 models through the headless runtime-model capture path,
completing C4a with visible proof.

## Context to read (and only this)

- `docs/roadmap/deep-dives/ee2-gamebryo-model-storage.md` sections 9 and 10.
- `src/Panopticon.Tools.TestHarness/AGENTS.md`: visual-validation and capture requirements.
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs` and `Program.cs`: scenario dispatch.
- `src/Panopticon.Tools.TestHarness/StandaloneModelCapture.cs`.
- Installed-asset setup and expected descriptors from `Ee2NifImportTests`.

## Steps

1. Add `Ee2NifProof` to `BuiltInScenario` and dispatch it from `Program`.
2. Require `CNC_GENERALS_PATH` for the rendering host and `EMPIRE_EARTH_II_PATH` for source
   models. Report each missing prerequisite directly.
3. Open `graphics.zip`, import each target, and materialize it through
   `ImportedModelMaterializer`. Do not register NIF in a shipped game definition.
4. Capture one isolated PNG per target plus a `validation_guide.md` under the normal temporary
   frame directory using `StandaloneModelCapture`.
5. Render fixed-function diffuse colors with texture-stage count zero. Preserve source texture
   names in imported descriptors.
6. Inspect both captures. Verify recognizable complete geometry, correct winding and normals,
   upright orientation and scale, baked child placement, and highest-detail Bomber geometry.
7. Add focused tests for argument dispatch and missing-prerequisite reporting without requiring a
   GPU.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`
- `src/Panopticon.Tools.TestHarness/Program.cs`
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs` (new)
- `test/Panopticon.Tools.ReplaySketch.Tests/Ee2NifProofScenarioTests.cs` (new)

## Acceptance criteria

- [ ] The War Galley and Twin-Engine Bomber each produce a readable captured frame.
- [ ] Both captures pass every visual check in the generated guide.
- [ ] The runtime path is `NifModelImporter` to `ImportedModelMaterializer` to `Model`.
- [ ] Missing install/archive/model prerequisites are reported explicitly.
- [ ] No EE2 asset is copied or committed.
- [ ] C4a is marked `Done` only after both captures pass.
- [ ] Standard validation passes.

## Validation commands

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario Ee2NifProof
dotnet build src
dotnet test src --filter "FullyQualifiedName~Ee2NifProofScenarioTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add a dedicated model viewer, EE2 game definition, NIF loader to shipped games, or
  ReplaySketch import UI.
- Do not decode `.texcache`, `.pcpatch`, skinning, or animation.
- Do not modify C4 glTF scope.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, set C4a Status to `Done` and tick C4a in the implementation checklist.
- Append a result line to `.exchange`, including capture paths and per-frame verdicts.
