# Task Card `WI-C4d-01`: Fix the NIF rotation convention

| Field | Value |
|---|---|
| Card ID | `WI-C4d-01` |
| Roadmap phase | [Track C · C4d](../../track-c-external-assets.md): EE2 skinned character animation |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [C4d architecture diagrams](../../deep-dives/c4d-ee2-skinned-animation.md#12-architecture-diagrams) |

## Goal

Read every NIF `Matrix33` as a column-vector rotation by transposing it once when it enters a
row-vector `Matrix4x4`, and regenerate the C4a and C4c locked outputs that change.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4d-ee2-skinned-animation.md` section 5.
- `src/OpenSage.Game/Content/Importing/Nif/NifTransformConverter.cs:42-68`.
- `src/OpenSage.Game/Content/Importing/Nif/NifRotationSampler.cs`: `EulerToMatrix` and
  `ToQuaternion`.
- `src/OpenSage.Game/Content/Importing/Nif/NifAnimationConverter.cs:356-368`.
- `src/OpenSage.Game.Tests/Content/Importing/NifModelImporterTests.cs`,
  `NifKeyCurveTests.cs`, `NifAnimationImporterTests.cs`, `Ee2NifImportTests.cs`, and
  `Ee2KfAnimationImportTests.cs`.
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs`.

## Steps

1. In `NifTransformConverter.CreateLocalMatrix`, copy the rotation transposed:
   `m.M12 = rotation.M21 * scale`, `m.M21 = rotation.M12 * scale`, and so on for every
   off-diagonal pair. Update the XML summary to say the stored matrix is column-vector.
2. In `NifRotationSampler.ToQuaternion`, copy the Euler matrix transposed the same way, so Euler
   samples stay consistent with node rests. Leave `EulerToMatrix` unchanged.
3. Leave quaternion keys and `ConjugateSourceRotationToEngine` unchanged.
4. Add a synthetic test: a node rotated 90 degrees about X by the stored matrix
   `[[1,0,0],[0,0,-1],[0,1,0]]` maps a child at source `(0, 1, 0)` to source `(0, 0, 1)`.
5. Add a synthetic test: a quaternion key equal to a non-symmetric node rest produces an identity
   rotation offset.
6. Run the installed C4a and C4c tests. Where locked AABBs, bone rests, or pose assertions change,
   recompute the values and replace them. Record every changed value in the `.exchange` line.
7. Regenerate the `Ee2NifProof` captures and inspect them. The Bomber pilots must sit upright in
   the cockpit and the Galley oars must reach the water.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/Nif/NifTransformConverter.cs`
- `src/OpenSage.Game/Content/Importing/Nif/NifRotationSampler.cs`
- `src/OpenSage.Game.Tests/Content/Importing/NifModelImporterTests.cs`
- `src/OpenSage.Game.Tests/Content/Importing/NifAnimationImporterTests.cs`
- `src/OpenSage.Game.Tests/Content/Importing/Ee2NifImportTests.cs`
- `src/OpenSage.Game.Tests/Content/Importing/Ee2KfAnimationImportTests.cs`

## Acceptance criteria

- [ ] The 90-degree X rotation test maps `+Y` to `+Z` in source space.
- [ ] A quaternion key equal to a non-symmetric rest yields an identity offset.
- [ ] Galley Idle offsets remain identity within 1e-4.
- [ ] Changed C4a and C4c locked values are recomputed, not loosened, and listed in `.exchange`.
- [ ] Regenerated captures show upright Bomber pilots and Galley oars reaching the water.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Nif"
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2"
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet run --project src/OpenSage.Tools.TestHarness -- --headless --built-in-scenario Ee2NifProof
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change the source-to-engine matrix. If captures show a facing or up-axis error after
  the fix, stop and escalate with the captures.
- Do not change quaternion key decoding.
- Do not add skinning.
- Do not commit captures or EE2 assets.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C4d-02` and `WI-C4d-03` are done, change `WI-C4d-06` to `Ready`.
- Append a result line to `.exchange`, including changed locked values and capture paths.
