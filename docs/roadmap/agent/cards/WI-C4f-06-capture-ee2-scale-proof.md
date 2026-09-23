# Task Card `WI-C4f-06`: Capture EE2 scale proof

| Field | Value |
|---|---|
| Card ID | `WI-C4f-06` |
| Roadmap phase | [Track C · C4f](../../track-c-external-assets.md): EE2 scale animation |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4f-05` |
| Diagrams | [C4f architecture diagrams](../../deep-dives/c4f-ee2-scale-animation.md#12-architecture-diagrams) |

## Goal

Extend the EE2 TestHarness proof scenario with deterministic textured captures that visibly show
scale animation and unchanged lighting under uniform scale.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4f-ee2-scale-animation.md` sections 2, 8, and 10.
- `src/OpenSage.Tools.TestHarness/Ee2NifProofScenario.cs`: C4c animated capture plan after
  `WI-C4c-06`.
- `src/OpenSage.Tools.TestHarness/StandaloneModelCapture.cs:41-95`: `configureInstance` hook.
- `src/OpenSage.Tools.TestHarness/ExternalModels/ExternalModelSceneRenderer.cs:100-120`:
  `PlayToTime` sampling.
- `src/OpenSage.Tools.ReplaySketch.Tests/Ee2NifProofScenarioTests.cs`: capture plan tests.

## Steps

1. Add scale proof capture entries:
   - `AB14_stealthbomber_takeoff` at the first key and the fully extended gear time;
   - `AB15_orbital_takeoff` at the first key and the fully extended gear time;
   - `afb13_StrikeFighter_takeoff` at minimum rear-wheel scale and at scale 1;
   - `CIV1_RoyalChariot_Egyptian_idle01` at time 0 for positive rest-scale parity.
2. Import each clip with `NifAnimationImporter`, materialize it, and pose the instance through
   `configureInstance` and `PlayToTime`.
3. Name outputs `<model>-<clip>-scale-<milliseconds>.png`.
4. Add validation-guide text naming the target node and expected visual scale change.
5. Extend `Ee2NifProofScenarioTests` so plan entries, names, clip keys, and guide text are covered
   without a GPU.
6. Keep existing C4b and C4c capture framing and output names unchanged.

## Files likely to change

- `src/OpenSage.Tools.TestHarness/Ee2NifProofScenario.cs`
- `src/OpenSage.Tools.ReplaySketch.Tests/Ee2NifProofScenarioTests.cs`

## Acceptance criteria

- [ ] Stealth bomber and orbital takeoff captures show landing gear scale changing.
- [ ] Strike fighter captures show the rear wheel scale changing from minimum to full size.
- [ ] Royal Chariot capture preserves positive rest-scale placement.
- [ ] Lighting on scaled parts stays stable enough for fixed-function visual validation.
- [ ] Repeat runs produce identical PNGs, and no EE2 asset is copied or committed.
- [ ] Standard validation passes.

## Validation commands

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet run --project src/OpenSage.Tools.TestHarness -- \
  --headless --built-in-scenario Ee2NifProof
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add C4d skinned scale captures.
- Do not add DDF state playback, sound, FX, or ReplaySketch Model-tab NIF support.
- Do not change existing C4c capture timings except to add scale proof frames.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, set C4f Status to `Done` and tick C4f in the implementation checklist.
- Append a result line to `.exchange`, including capture paths and verdicts.
