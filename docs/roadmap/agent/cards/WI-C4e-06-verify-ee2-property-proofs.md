# Task Card `WI-C4e-06`: Verify EE2 property proofs

| Field | Value |
|---|---|
| Card ID | `WI-C4e-06` |
| Roadmap phase | [Track C · C4e](../../track-c-external-assets.md): EE2 property animation |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4e-04`, `WI-C4e-05` |
| Diagrams | [C4e architecture diagrams](../../deep-dives/c4e-ee2-property-animation.md#11-architecture-diagrams) |

## Goal

Add installed-asset assertions and deterministic TestHarness captures that prove visibility, texture transform, material color, and alpha animation work end-to-end.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4e-ee2-property-animation.md` sections 2 and 9.
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifPropertyAnimationTests.cs` from `WI-C4e-04`.
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs`.
- `src/Panopticon.Tools.TestHarness/StandaloneModelCapture.cs:41-95`.
- `src/Panopticon.Tools.TestHarness/ExternalModels/ExternalModelSceneRenderer.cs:100-120`.
- `test/Panopticon.Tools.ReplaySketch.Tests/Ee2NifProofScenarioTests.cs`.

## Steps

1. Extend installed-asset tests to import, materialize, bind, and sample the four proof clips named in the deep dive.
2. Assert sampled property state at fixed times: Market visibility at 0 and 0.033333 s, EIFV tread U offset at 0 and 1 s, Stealth Fighter Bomber ambient and specular colors at 0, and ResourceFish alpha at 0, 2, and 4 s.
3. Add an `Ee2PropertyProof` capture plan with one static baseline frame and at least two animated frames per visual family.
4. Pose instances through `CreateAnimationInstance` and fixed-time playback. Reuse the C4b texture setup.
5. Extend the GPU-free scenario tests for capture names, clip keys, sample times, and validation-guide text.
6. Update the card and phase statuses when captures and tests pass.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifPropertyAnimationTests.cs`
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs`
- `test/Panopticon.Tools.ReplaySketch.Tests/Ee2NifProofScenarioTests.cs`

## Acceptance criteria

- [ ] Market captures show open and close visibility changes.
- [ ] Egyptian EIFV captures show moving tread texture coordinates.
- [ ] Stealth Fighter Bomber captures render the imported material color state.
- [ ] ResourceFish captures show alpha fade at sampled times.
- [ ] Repeat runs produce deterministic outputs, and no EE2 asset is copied or committed.
- [ ] Standard validation passes.

## Validation commands

```sh
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2NifPropertyAnimationTests"
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario Ee2PropertyProof
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add EE2 DDF state playback.
- Do not add ReplaySketch Model-tab UI work.
- Do not loosen deterministic-capture expectations to hide rendering defects.
- Do not copy captures or EE2 source assets into version control.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, set C4e Status to `Done` and tick C4e in the implementation checklist.
- Append a result line to `.exchange`, including capture paths and verdicts.
