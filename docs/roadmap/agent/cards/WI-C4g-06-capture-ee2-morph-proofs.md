# Task Card `WI-C4g-06`: Capture EE2 morph proofs

| Field | Value |
|---|---|
| Card ID | `WI-C4g-06` |
| Roadmap phase | [Track C · C4g](../../track-c-external-assets.md): EE2 morph animation |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4g-05` |
| Diagrams | [C4g architecture diagrams](../../deep-dives/c4g-ee2-morph-animation.md#10-architecture-diagrams) |

## Goal

Prove the EE2 morph path with installed-asset tests and deterministic TestHarness captures for
the farm yam seeding morph.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4g-ee2-morph-animation.md` sections 2, 9, and the reproduction
  appendix.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` section 8.
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifImportTests.cs`.
- `src/Panopticon.Tools.TestHarness/ExternalModels/ExternalModelSceneRenderer.cs`.
- `src/Panopticon.Tools.TestHarness/StandaloneModelCapture.cs`.
- `src/Panopticon.Tools.TestHarness/Scenarios/`.

## Steps

1. Change Status to `In progress`.
2. Add installed-asset assertions for `bld_farm_yam#morph-kf=Graphics/Building/bld_farm_yam.kf`.
3. Assert the imported target count, vertex count, nonzero delta count, max delta, and sampled
   weights at 0, half duration, and end.
4. Add a TestHarness proof scenario that captures `bld_farm_yam` at weights 0, 0.5, and 1.
5. Add an embedded morph smoke test for `fx_muzzle_cannon.nifcache` or a reduced synthetic
   fixture if the installed effect model is blocked by unrelated FX-only data.
6. Verify that disabling the `morph-targets` gate produces the documented blocking diagnostic.
7. Record the capture output path and visual result in the test log without committing images.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifImportTests.cs`
- `src/Panopticon.Tools.TestHarness/Scenarios/Ee2NifProofScenario.cs`
- `src/Panopticon.Tools.TestHarness/ExternalModels/ExternalModelSceneRenderer.cs`

## Acceptance criteria

- [ ] Installed tests pass when the EE2 install is available.
- [ ] Captures show visible farm yam shape change between weights 0, 0.5, and 1.
- [ ] The embedded morph smoke test covers at least one quadratic weight curve.
- [ ] Gate-disabled import fails before GPU allocation with a morph-target diagnostic.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" dotnet test src --filter "FullyQualifiedName~Ee2NifImportTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not commit captured images or EE2 assets.
- Do not expand the phase to NIF 10.0.1.0.
- Do not add full EE2 DDF state-machine playback.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, mark C4g Done in the roadmap propagation pass.
- Append a result line to `.exchange`.
