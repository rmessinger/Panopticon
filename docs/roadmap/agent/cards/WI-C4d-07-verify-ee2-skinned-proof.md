# Task Card `WI-C4d-07`: Verify the EE2 skinned proof

| Field | Value |
|---|---|
| Card ID | `WI-C4d-07` |
| Roadmap phase | [Track C · C4d](../../track-c-external-assets.md): EE2 skinned character animation |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4d-04`, `WI-C4d-06` |
| Diagrams | [C4d architecture diagrams](../../deep-dives/c4d-ee2-skinned-animation.md#12-architecture-diagrams) |

## Goal

Prove the British Druid imports, binds its five DDF-bound clips, and deforms correctly in
deterministic textured captures, and that the negative fixtures fail per contract.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4d-ee2-skinned-animation.md` sections 2, 9, and 11.
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifImportTests.cs` and
  `Ee2KfAnimationImportTests.cs`.
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs`.
- `src/Panopticon.Tools.TestHarness/StandaloneModelCapture.cs:41-95`.
- `src/Panopticon.Tools.TestHarness/ExternalModels/ExternalModelSceneRenderer.cs:100-120`.
- `test/Panopticon.Tools.ReplaySketch.Tests/Ee2NifProofScenarioTests.cs`.

## Steps

1. Add `Ee2SkinnedImportTests` (interactive). Import `CIV1_Druid_british` and assert 32 bones,
   one skinned LOD0 sub-object with 648 vertices, the influence histogram 486 / 158 / 2 / 2, and
   nonzero third and fourth weights on vertices 151 and 153.
2. Assert bind-pose parity within 1e-4 on the installed model.
3. Import, materialize, and bind all five clips in section 2 through
   `ModelInstance.CreateAnimationInstance`. Assert identity offsets at `walk01` time 0 for every
   channel whose time-0 key equals its rest.
4. Assert that `lsf7_priest_fe` throws `NotSupportedException` naming `body_LOD0` and five
   influences, and that `llm2_mountedslinger_fe` throws `InvalidDataException` naming
   `horse_LOD0`.
5. Extend `Ee2NifProofScenario` with the section 11 druid capture plan. Pose through
   `CreateAnimationInstance` and fixed-time playback, and reuse the C4b texture setup.
6. Extend the GPU-free scenario tests for capture names, clip keys, and sample times.
7. Inspect the captures: upright Druid, feet on the ground, limbs deforming, no stretched or
   detached vertices.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2SkinnedImportTests.cs` (new)
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs`
- `test/Panopticon.Tools.ReplaySketch.Tests/Ee2NifProofScenarioTests.cs`

## Acceptance criteria

- [x] All installed assertions in steps 1 to 4 pass.
- [x] Captures show the upright, textured Druid at rest and deforming through Walk, Attack1, and
      Death.
- [x] Repeat runs produce identical captures.
- [x] No EE2 asset or capture is committed.
- [x] Standard validation passes.

## Validation commands

```sh
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2SkinnedImportTests"
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario Ee2NifProof
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add DDF state playback, sound or FX text keys, or player-color patches.
- Do not add ReplaySketch Model-tab NIF support.
- Do not loosen capture determinism to hide rendering defects.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, set C4d Status to `Done` in the track doc and tick C4d in the implementation
  checklist.
- Append a result line to `.exchange`, including capture paths and verdicts.
