# Task Card `WI-C1-14`: Remove legacy W3D runtime conversion

| Field | Value |
|---|---|
| Card ID | `WI-C1-14` |
| Roadmap phase | [Track C · C1](../../track-c-external-assets.md): Format-agnostic import abstraction |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C1-13` |
| Diagrams | [C1 architecture diagrams](../../deep-dives/c1-import-abstraction.md#9-architecture-diagrams) |

## Goal

Remove the parity-proven direct W3D-to-runtime path so model and animation loading has one route
through the C1 importer contracts and engine materializers.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 7-9.
- Passing `W3dImportParityTests` from `WI-C1-12`.
- Remaining `OpenSage.FileFormats.W3d` references under `src/OpenSage.Game/Graphics/`.
- Legacy branches in `src/OpenSage.Game/Content/Loaders/`.
- `docs/roadmap/seams.md`: model / animation import row.

## Steps

1. Remove W3D constructors and partial conversion files from runtime model and animation types.
2. Remove legacy loader constructors, conversion branches, and now-unused W3D imports.
3. Search `src/OpenSage.Game/Graphics/` for `OpenSage.FileFormats.W3d`; the result must be empty.
4. Run the parity tests after cleanup without changing expected descriptors.
5. Update `seams.md` from planned to current and mark C1 complete in the track doc and
   implementation checklist.

## Files likely to change

- `src/OpenSage.Game/Graphics/Model*.cs`
- `src/OpenSage.Game/Graphics/Animation/`
- `src/OpenSage.Game/Content/Loaders/`
- `docs/roadmap/seams.md`
- `docs/roadmap/track-c-external-assets.md`
- `docs/roadmap/implementation-checklist.md`

## Acceptance criteria

- [ ] No runtime graphics type constructs itself from a W3D parser type.
- [ ] All on-demand model and animation loading flows through the importer contracts.
- [ ] W3D parity tests pass unchanged after legacy removal.
- [ ] C1 is marked `Done` only after cleanup and standard validation pass.
- [ ] Standard validation passes.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "FullyQualifiedName~W3dImportParityTests"
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not alter parity expectations during cleanup.
- Do not fix unrelated W3D parser defects.
- Do not add glTF, retargeting, PBR, or preview-tool behavior.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, set C1 Status to `Done`, tick C1 in the checklist, and unblock `WI-A4-36`.
- If done, change `WI-C2-01`, `WI-C3-01`, `WI-C4-01`, and `WI-C4-03` to `Ready`.
- If done and `WI-C4a-09` and `WI-C4a-10` are done, change `WI-C4a-11` to `Ready`.
- Append a result line to `.exchange`.
