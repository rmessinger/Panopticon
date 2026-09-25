# Task Card `WI-C4f-03`: Convert KF scale keys

| Field | Value |
|---|---|
| Card ID | `WI-C4f-03` |
| Roadmap phase | [Track C · C4f](../../track-c-external-assets.md): EE2 scale animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4f-01`, `WI-C4f-02` |
| Diagrams | [C4f architecture diagrams](../../deep-dives/c4f-ee2-scale-animation.md#12-architecture-diagrams) |

## Goal

Convert non-unit NIF `NiKeyframeData` scale groups into C1 `Scale` channels by reusing C4c key
curve evaluation and rest-relative offset rules.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4f-ee2-scale-animation.md` sections 4, 5, 7, and 9.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 4.2, 6.2, and 6.3.
- `src/Panopticon.FileFormats.Nif/NifKeys.cs`: key group types after `WI-C4c-01`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifKeyCurve.cs`: C4c float curve evaluator after
  `WI-C4c-03`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifAnimationConverter.cs`: C4c channel conversion after
  `WI-C4c-04`.

## Steps

1. Replace C4c's non-unit scale rejection with a call to a new scale conversion helper.
2. Keep identity scale groups accepted and dropped.
3. Validate scale keys: finite, non-negative, key times within clip duration, key type linear or
   quadratic only.
4. Emit linear scale groups at source key times with `sampleScale / restScale` values.
5. Emit quadratic scale groups at C4c's 30 Hz resampling time set plus source key times, evaluated
   through `NifKeyCurve.Evaluate`.
6. Reject a target whose rest scale is non-positive, naming the target and rest scale.
7. Update translation-offset computation for scaled rests by applying the inverse rest scale before
   inverse rest rotation.
8. Add synthetic tests for unit rest, positive non-unit rest, zero animated sample, quadratic
   midpoint evaluation, TBC rejection, and singular-rest rejection.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Nif/NifAnimationConverter.cs`
- `src/Panopticon.Simulation/Content/Importing/Nif/NifAnimationImporter.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/NifAnimationImporterTests.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/NifKeyCurveTests.cs`

## Acceptance criteria

- [ ] Scale channel values reconstruct source scale by `offsetScale * restScale` within 1e-4.
- [ ] Quadratic scale uses the same Hermite tangent convention as C4c Euler curves.
- [ ] Translation offsets still reconstruct source samples for unit-rest and scaled-rest targets.
- [ ] Singular rest scale fails before channel emission and names the target.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifAnimationImporterTests|FullyQualifiedName~NifKeyCurveTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add TBC evaluation.
- Do not resample linear scale keys.
- Do not add DDF state binding or TestHarness captures.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4f-04` to `Ready`.
- Append a result line to `.exchange`.
