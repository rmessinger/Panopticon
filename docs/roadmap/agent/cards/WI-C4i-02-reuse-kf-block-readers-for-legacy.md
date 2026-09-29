# Task Card `WI-C4i-02`: Reuse KF block readers for legacy

> Audience: agent task card.

| Field | Value |
|---|---|
| Card ID | `WI-C4i-02` |
| Roadmap phase | [Track C · C4i](../../track-c-external-assets.md): EE2 legacy KF formats |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4i-01`, `WI-C4c-01` |
| Diagrams | [C4i architecture diagrams](../../deep-dives/c4i-ee2-legacy-kf-formats.md#10-architecture-diagrams) |

## Goal

Allow the C4c KF block readers to decode NIF 10.0.1.0 keyframe-only files under the verified
nif.xml field conditions.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4i-ee2-legacy-kf-formats.md` sections 3.1, 4.2, and 8.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 3 and 4.
- KF block types added by `WI-C4c-01` under `src/Panopticon.FileFormats.Nif/Blocks/`.
- `src/Panopticon.FileFormats.Nif/NifFile.cs:118-180`.
- `test/Panopticon.Simulation.Tests/Data/Nif/NifKeyframeTests.cs`.

## Steps

1. Pass the parsed NIF version layout into KF block readers that have version-conditioned fields.
2. Keep `NiControllerSequence` on the pre-interpolator layout for 10.0.1.0: name,
   accumulation-root name, text-key ref, count, and target-name/controller-ref pairs.
3. Keep `NiKeyframeController` on the pre-interpolator layout for 10.0.1.0: time-controller
   fields plus direct `NiKeyframeData` ref.
4. Keep the XYZ rotation order `f32` in `NiKeyframeData` for 10.0.1.0.
5. Add synthetic 10.0.1.0 keyframe-only streams for a quaternion controller, an XYZ quadratic
   controller, and a two-root footer.
6. Add a rejection fixture for one legacy unsupported block name such as `NiMaterialColorController`.

## Files likely to change

- `src/Panopticon.FileFormats.Nif/NifFile.cs`
- `src/Panopticon.FileFormats.Nif/Blocks/NifControllerSequence.cs`
- `src/Panopticon.FileFormats.Nif/Blocks/NifKeyframeController.cs`
- `src/Panopticon.FileFormats.Nif/Blocks/NifKeyframeData.cs`
- `test/Panopticon.Simulation.Tests/Data/Nif/NifTestDataBuilder.cs`
- `test/Panopticon.Simulation.Tests/Data/Nif/NifKeyframeTests.cs`

## Acceptance criteria

- [ ] The same reader types expose both 10.1.0.0 C4c and 10.0.1.0 C4i keyframe blocks.
- [ ] No interpolator or string-palette fields are read for 10.0.1.0 controlled blocks.
- [ ] Wrong-type references still fail validation with asset context.
- [ ] Unsupported legacy controller blocks fail explicitly and are not skipped.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifKeyframeTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add material color, visibility, UV, morph, or particle controller support.
- Do not implement NetImmerse 4.2.2.0 inline block dispatch.
- Do not change C4c key evaluation.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4c-04` is done, change `WI-C4i-03` to `Ready`.
- If done, change `WI-C4i-05` to `Ready` when its C4c prerequisite is done.
- Append a result line to `.exchange`.
