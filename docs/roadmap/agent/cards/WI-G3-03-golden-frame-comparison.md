# Task Card `WI-G3-03`: Deterministic per-preset capture pass over the fixed scene set

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G3-03` |
| Roadmap phase | [Track G · G3](../../track-g-rendering-clarity.md): Visual regression harness |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-G3-01`, `WI-G3-02`, `WI-G2-03`, `WI-G1-04` |
| Diagrams | [G3 architecture](../../deep-dives/g3-visual-regression-harness.md#5-architecture-diagrams) |

## Goal

Add the `VisualRegression` TestHarness built-in scenario in capture-only form: render the fixed
scene set headlessly under each preset at viewport scale 1 and 2 with a fixed render clock, score
every capture with the `WI-G3-02` metrics, and write the captures and a `report.json`. Golden
comparison and the gate are `WI-G3-04`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/g3-visual-regression-harness.md` §3.1 to §3.3 and §3.6, and the
  architecture diagrams.
- `docs/roadmap/implementation/track-g-g3-visual-regression.md`: the `WI-G3-01` scene
  repeatability table.
- `src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs:35-62,105-136,487-511,1174-1214`:
  viewport size, `Run`, `Game` construction, camera setup, `CaptureRunSettings`.
- `src/Panopticon.Simulation/Game.cs:364-386,951-955,1002-1011`: render and logic timers,
  `EnableFixedLogicTimestep`, `Render`.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/Capture/CaptureProfile.cs`: `CaptureProfile`,
  `CaptureCamera`.
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs` and `Program.cs:213-240`: scenario dispatch.
- The metrics type added by `WI-G3-02`.

## Steps

1. Add `Game.EnableFixedRenderTimestep`, setting the render `DeltaTimer.FixedDelta`, mirroring
   `EnableFixedLogicTimestep`.
2. Add optional `PitchDegrees` and `AngleDegrees` to `CaptureCamera`, applied after
   `SetAngleAndPitchToDefault` in `Explicit` mode. Absent values keep today's behaviour.
3. Add the active `GraphicsSettings` preset and an integer `ViewportScale` (default 1) to
   `CaptureRunSettings`; the runner constructs `Game` with the preset and renders at
   `1024x768 * ViewportScale`.
4. Add `visual-regression-scenes.json` and one `vr-<scene>.json` `CaptureProfile` per scene from
   the deep dive table to the ReplaySketch profile store. Drop any scene `WI-G3-01` recorded as not
   repeatable, and note it in the implementation plan.
5. Add `BuiltInScenario.VisualRegression` and a `VisualRegressionRunner` that, for each preset and
   scene, captures at scale 1 and 2, box-downsamples the scale-2 capture, scores both with the
   metrics, and writes captures and `report.json` to an output directory.
6. Exit 5 when, for any scene, the supersampled reference does not score lower on edge aliasing
   than the native capture over the scene's designated silhouette regions.

## Files likely to change

- `src/Panopticon.Simulation/Game.cs`
- `src/Panopticon.Tools.TestHarness/` (`BuiltInScenario.cs`, `Program.cs`, `ScenarioCaptureRunner.cs`,
  new `VisualRegressionRunner.cs`)
- `src/Panopticon.Tools.ReplaySketch.Core/Model/Capture/CaptureProfile.cs`
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/` (scene set and VR profiles)
- `test/Panopticon.Simulation.Tests/` (camera override and downsample unit tests)

## Acceptance criteria

- [ ] Two consecutive `VisualRegression` runs on the reference host write byte-identical captures
      for every scene and preset.
- [ ] The designated silhouette regions score lower on edge aliasing in the supersampled
      reference than in the native capture, under both presets.
- [ ] Profiles without `PitchDegrees`/`AngleDegrees` capture identically to the pre-card build.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario VisualRegression --headless
```

## Out of scope / guardrails

- No golden store, comparer or update mode; that is `WI-G3-04`.
- Do not build a new tool or viewer, per the [C5 Decision](../../open-questions.md).
- Do not change visual algorithms to improve repeatability. A fixed presentation clock is allowed;
  simulation behaviour is not changed.
- If a scene cannot be rendered headlessly, drop it, record why, and continue with the rest.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Change `WI-G3-04` from `Blocked` to `Ready` if its other prerequisites are `Done`.
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
