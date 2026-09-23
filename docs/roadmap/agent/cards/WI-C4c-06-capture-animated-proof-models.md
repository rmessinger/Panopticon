# Task Card `WI-C4c-06`: Capture the animated proof models

| Field | Value |
|---|---|
| Card ID | `WI-C4c-06` |
| Roadmap phase | [Track C · C4c](../../track-c-external-assets.md): EE2 rigid KF animation |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4c-05` |
| Diagrams | [C4c architecture diagrams](../../deep-dives/c4c-ee2-rigid-animation.md#10-architecture-diagrams) |

## Goal

Extend the `Ee2NifProof` TestHarness scenario with deterministic textured captures of the
proof models at fixed clip times.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` section 8.
- `src/OpenSage.Tools.TestHarness/Ee2NifProofScenario.cs`.
- `src/OpenSage.Tools.TestHarness/StandaloneModelCapture.cs:41-95` (`configureInstance`).
- `src/OpenSage.Tools.TestHarness/ExternalModels/ExternalModelSceneRenderer.cs:100-120`
  (`PlayToTime`).
- `src/OpenSage.Tools.ReplaySketch.Tests/Ee2NifProofScenarioTests.cs`.

## Steps

1. Add an animated capture plan:
   - Galley Walk at 0, 0.767, and 1.433 s;
   - Galley Idle at 0 s;
   - Bomber Walk at 0 and 1/12 s;
   - Bomber TakeOff at 0 and 1.333 s.
2. Import each clip with `NifAnimationImporter`, materialize it, and pose the instance through
   `configureInstance` with `PlayToTime`. Reuse the existing camera, yaw, lighting, and texcache
   installation.
3. Name outputs `<model>_<clip>_<milliseconds>.png`. Add a validation-guide line for each capture
   stating the expected pose.
4. Extend `Ee2NifProofScenarioTests` so plan entries, names, and guide text are covered without a
   GPU.

## Files likely to change

- `src/OpenSage.Tools.TestHarness/Ee2NifProofScenario.cs`
- `src/OpenSage.Tools.ReplaySketch.Tests/Ee2NifProofScenarioTests.cs`

## Acceptance criteria

- [ ] Galley Walk frames show different oar positions.
- [ ] Bomber Walk frames show propellers turned by a quarter turn.
- [ ] TakeOff 1.333 s shows the level, raised flight pose.
- [ ] Galley Idle and TakeOff at time 0 match the static captures.
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

- Do not add DDF state playback, sound, FX, or ReplaySketch Model-tab NIF support.
- Do not change the static capture plan's framing.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, set C4c Status to `Done` and tick C4c in the implementation checklist.
- Append a result line to `.exchange`, including capture paths and verdicts.
