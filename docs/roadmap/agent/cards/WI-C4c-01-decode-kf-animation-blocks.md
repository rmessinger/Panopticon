# Task Card `WI-C4c-01`: Decode KF animation blocks

| Field | Value |
|---|---|
| Card ID | `WI-C4c-01` |
| Roadmap phase | [Track C · C4c](../../track-c-external-assets.md): EE2 rigid KF animation |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4a-02` |
| Diagrams | [C4c architecture diagrams](../../deep-dives/c4c-ee2-rigid-animation.md#10-architecture-diagrams) |

## Goal

Extend `OpenSage.FileFormats.Nif` to read the four NIF 10.1 KF block types to exact end-of-file,
with validated references, using synthetic byte streams only.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 1, 3, and 7.
- `src/OpenSage.FileFormats.Nif/NifFile.cs:21-55` (block loop and footer), `118-155` (dispatch),
  and `157-312` (reference validation and roots).
- `src/OpenSage.FileFormats.Nif/NifBinaryReader.cs` and `Blocks/NifStringExtraData.cs` (read
  pattern for a non-`NiObjectNET` block).
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs:131-207` (block builders and footer).

## Steps

1. Add block types under `Blocks/`: `NifControllerSequence` with name, accumulation-root name,
   text-keys reference, and `NifControlledBlock` entries (target name, controller reference);
   `NifTextKeyExtraData` with name and `(time, value)` keys; `NifKeyframeController` with the
   section 3 time-controller fields and data reference; `NifKeyframeData`.
2. Add `NifKeyType` (`Linear = 1`, `Quadratic = 2`, `Tbc = 3`, `XyzRotation = 4`,
   `Constant = 5`), `NifKey<T>` (time, value, optional forward and backward), `NifQuaternionKey`,
   and `NifKeyGroup<T>` (interpolation, keys). Group small supporting types per the repository
   one-type-per-file rule.
3. Decode key groups for `float` and `Vector3` with linear and quadratic keys, quaternion
   rotation with linear keys, and XYZ rotation (count 1, one order `f32`, three float groups).
   Throw `NotSupportedException` naming the block for TBC, constant, and unknown key types.
4. Register the four types in `ReadBlock`.
5. Validate references: text keys must be `NifTextKeyExtraData`, controllers
   `NifKeyframeController`, and keyframe data `NifKeyframeData`. Leave `Roots` NiNode-only, so it
   is empty for KF files.
6. Add `NifKeyframeTests` with builders in `NifTestDataBuilder`. Cover a sequence with one
   quaternion channel and one XYZ quadratic channel, exact consumption, a bad controller
   reference, and a TBC rejection.

## Files likely to change

- `src/OpenSage.FileFormats.Nif/NifFile.cs`
- `src/OpenSage.FileFormats.Nif/Blocks/NifControllerSequence.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifTextKeyExtraData.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifKeyframeController.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifKeyframeData.cs` (new)
- `src/OpenSage.FileFormats.Nif/NifKeys.cs` (new)
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs`
- `src/OpenSage.Game.Tests/Data/Nif/NifKeyframeTests.cs` (new)

## Acceptance criteria

- [ ] Synthetic KF streams parse to exact end-of-file and expose every section 3 field.
- [ ] Quadratic keys keep forward and backward values exactly as stored. Interpretation belongs
      to `WI-C4c-03`.
- [ ] Wrong-type references throw `InvalidDataException`, and TBC or constant keys throw
      `NotSupportedException`.
- [ ] Existing C4a and C4b parser tests pass unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifKeyframeTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not evaluate curves, convert coordinates, or reference `OpenSage.Game`.
- Do not add other controller families or NIF 10.0.1.0 support.
- Do not commit or embed extracted EE2 bytes.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4c-03` to `Ready`.
- Append a result line to `.exchange`.
