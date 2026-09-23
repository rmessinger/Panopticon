# Task Card `WI-C4c-03`: Evaluate NIF key curves

| Field | Value |
|---|---|
| Card ID | `WI-C4c-03` |
| Roadmap phase | [Track C · C4c](../../track-c-external-assets.md): EE2 rigid KF animation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4c-01` |
| Diagrams | [C4c architecture diagrams](../../deep-dives/c4c-ee2-rigid-animation.md#10-architecture-diagrams) |

## Goal

Add pure functions that evaluate NIF float curves and resample XYZ Euler rotation into ordered
source-frame quaternion samples.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 4.1, 4.2, and 6.3.
- Key types added by `WI-C4c-01` in `src/OpenSage.FileFormats.Nif/NifKeys.cs`.
- `src/OpenSage.FileFormats.Nif/NifMatrix33.cs` (stored rotation reading).

## Steps

1. Add `NifKeyCurve.Evaluate(NifKeyGroup<float>, float time)`. Linear keys interpolate
   linearly, and quadratic keys use segment-normalized Hermite with outgoing = first key
   backward and incoming = second key forward. Hold end values outside the key range.
2. Add `NifRotationSampler.EulerToMatrix(x, y, z)` returning the stored-matrix layout for
   column-vector `Rz(z) * Ry(y) * Rx(x)`.
3. Add `NifRotationSampler.SampleXyz(data, duration)`. Build the section 6.3 time set, evaluate
   the three axes, compose, convert to quaternions, flip consecutive samples into one hemisphere,
   and throw `NotSupportedException` when any step reaches 90 degrees.
4. Add tests:
   - `EulerToMatrix(2.3865, 0.5080, -1.1135)` matches the stored `R_OAR14` rest matrix from the
     C4a inventory to 2e-4.
   - A three-turn linear Y spin over one second yields 31 samples at 36 degree steps in one
     direction.
   - Hermite evaluation reproduces both keys and a hand-computed midpoint.
   - A two-key spin that turns more than 90 degrees in one sample is rejected.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/Nif/NifKeyCurve.cs` (new)
- `src/OpenSage.Game/Content/Importing/Nif/NifRotationSampler.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/NifKeyCurveTests.cs` (new)

## Acceptance criteria

- [ ] Evaluation is deterministic, allocation-light, and has no engine-frame conversion.
- [ ] The Euler order and Hermite tangent convention match the deep dive, locked by tests.
- [ ] Sample times contain `0`, `D`, every source key time, and every `k / 30` step exactly once.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifKeyCurveTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not build `ImportedAnimation`, compute rest offsets, or touch importer wiring.
- Do not add TBC evaluation.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4c-02` is done, change `WI-C4c-04` to `Ready`.
- Append a result line to `.exchange`.
