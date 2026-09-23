# Task Card `WI-C4h-06`: Capture particle proof effects

| Field | Value |
|---|---|
| Card ID | `WI-C4h-06` |
| Roadmap phase | [Track C · C4h](../../track-c-external-assets.md): EE2 particle effects |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4h-05` |
| Diagrams | [C4h architecture diagrams](../../deep-dives/c4h-ee2-particle-effects.md#11-architecture-diagrams) |

## Goal

Add deterministic TestHarness captures that prove the three DDF-bound EE2 particle effects render
through the existing OpenSAGE particle runtime.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4h-ee2-particle-effects.md` sections 2, 9, and 10.
- `src/OpenSage.Tools.TestHarness/StandaloneModelCapture.cs:41-95`.
- `src/OpenSage.Tools.TestHarness/ExternalModels/ExternalModelSceneRenderer.cs:100-120`.
- The C4h binding helper from `WI-C4h-05`.
- `src/OpenSage.Tools.ReplaySketch.Tests/Ee2NifProofScenarioTests.cs`.

## Steps

1. Add an `Ee2ParticleProof` scenario or extend the existing EE2 proof scenario with a separate
   particle-effect capture plan.
2. Spawn the proof effects with fixed seeds:
   - `fx_explosion_1` at a fixed world origin;
   - `fx_muzzle_tank` at a muzzle or root tag on a proof model;
   - `fx_watersplash_1` at a fixed water-splash origin.
3. Capture each effect at 0.0 s, 0.5 s, and 1.0 s, using fixed camera, light, and seed values.
4. Write a validation-guide entry for each frame that names the expected visible change.
5. Add non-GPU tests for the capture plan, output names, seeds, and validation-guide text.
6. Run the headless scenario with a local EE2 install and inspect the generated frames.
7. On success, set this card to `Done` and mark C4h complete in the phase owner docs.

## Files likely to change

- `src/OpenSage.Tools.TestHarness/Ee2ParticleProofScenario.cs` (new)
- `src/OpenSage.Tools.TestHarness/Program.cs` or scenario registration file
- `src/OpenSage.Tools.ReplaySketch.Tests/Ee2ParticleProofScenarioTests.cs` (new)

## Acceptance criteria

- [ ] Explosion frames show a visible expanding and fading particle effect.
- [ ] Muzzle-smoke frames spawn at the resolved model tag and follow that tag when configured.
- [ ] Water-splash frames show a vertical splash with stable seeded particle placement.
- [ ] Repeated runs with the same seed produce identical descriptors and stable captures.
- [ ] Unsupported proof-adjacent features still fail explicitly.
- [ ] Standard validation passes.

## Validation commands

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet run --project src/OpenSage.Tools.TestHarness -- \
  --headless --built-in-scenario Ee2ParticleProof
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add visual baselines before seed control is proven.
- Do not implement full EE2 visual tables or sound text keys.
- Do not copy EE2 assets into the repository.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, set C4h Status to `Done` and tick C4h in the implementation checklist.
- Append a result line to `.exchange`, including capture paths and verdicts.
