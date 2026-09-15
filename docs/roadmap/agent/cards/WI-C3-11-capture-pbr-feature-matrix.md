# Task Card `WI-C3-11`: Capture the PBR feature matrix

| Field | Value |
|---|---|
| Card ID | `WI-C3-11` |
| Roadmap phase | [Track C · C3](../../track-c-external-assets.md): Material and texture mapping |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C3-10`, `WI-C4a-10` |
| Diagrams | [C3 architecture diagrams](../../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams) |

## Goal

Render deterministic captures that cover the complete C3 visual feature matrix and color-space
contract.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` sections 6 and 8.
- Synthetic PBR fixture from `WI-C3-10`.
- `src/OpenSage.Tools.TestHarness/AGENTS.md`.
- `src/OpenSage.Tools.TestHarness/BuiltInScenario.cs` and `Program.cs`.
- `src/OpenSage.Tools.TestHarness/StandaloneModelCapture.cs`.
- `src/OpenSage.Tools.ReplaySketch.Tests/StandaloneModelCaptureTests.cs`.
- `docs/screenshots.md`.

## Steps

1. Add `PbrMaterialProof` to `BuiltInScenario` and dispatch it from `Program`.
2. Require `CNC_GENERALS_PATH` for the rendering host, then build deterministic synthetic runtime
   models and textures for dielectric, metal/roughness contrast, normal mapping, emissive, alpha
   mask, alpha blend, double-sided, UV selection, sampler addressing, and nearest/linear filtering.
3. Capture static and skinned models through `StandaloneModelCapture`.
4. Add an emissive-only color-space model with a known sRGB source texel and assert the captured
   channel round-trips within one 8-bit step.
5. Write one `validation_guide.md` naming the expected check for every PNG and inspect every
   capture.
6. Add focused argument-dispatch and missing-prerequisite tests that do not require a GPU.

## Files likely to change

- `src/OpenSage.Tools.TestHarness/BuiltInScenario.cs`
- `src/OpenSage.Tools.TestHarness/Program.cs`
- `src/OpenSage.Tools.TestHarness/PbrMaterialProofScenario.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Tests/PbrMaterialProofScenarioTests.cs` (new)
- `src/OpenSage.Tools.TestHarness/AGENTS.md`
- `docs/screenshots.md` only if the capture workflow gains a new reusable capability

## Acceptance criteria

- [ ] Captures cover every feature listed in C3 deep-dive section 8.
- [ ] The known sRGB emissive texel matches within one output step.
- [ ] Static and skinned paths render through the same PBR material family.
- [ ] Repeated captures are deterministic.
- [ ] Standard validation passes.

## Validation commands

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet run --project src/OpenSage.Tools.TestHarness -- \
  --headless --built-in-scenario PbrMaterialProof
dotnet build src
dotnet test src --filter "FullyQualifiedName~PbrMaterialProofScenarioTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add a dedicated model viewer.
- Do not commit or load operator-supplied model or texture assets; the Generals install is only
  the TestHarness rendering host.
- Do not expand C3 into advanced PBR extensions or renderer-wide G6 color-space work.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, set C3 Status to `Done` and tick C3 in the implementation checklist.
- Append a result line to `.exchange`, including the capture output and validation result.
