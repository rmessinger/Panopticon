# Task Card `WI-C2-01`: Add imported animation targets

| Field | Value |
|---|---|
| Card ID | `WI-C2-01` |
| Roadmap phase | [Track C · C2](../../track-c-external-assets.md): Skeleton and animation retargeting |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C1-14` |
| Diagrams | [C2 architecture diagrams](../../deep-dives/c2-skeleton-animation-retargeting.md#8-architecture-diagrams) |

## Goal

Represent imported animation channels with an explicit direct-index or exact bone-name target
without changing W3D output.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c2-skeleton-animation-retargeting.md` sections 1-3 and 5.
- `src/OpenSage.Game/Content/Importing/ImportedAnimationChannel.cs`.
- `src/OpenSage.Game/Content/Importing/W3d/W3dAnimationImporter.cs`.
- `src/OpenSage.Game.Tests/Content/Importing/ImportedAssetTests.cs`.
- `src/OpenSage.Game.Tests/Content/Importing/W3dAnimationImporterTests.cs`.

## Steps

1. Add immutable `ImportedAnimationTarget` and `ImportedAnimationTargetKind` types with
   `DirectIndex` and `BoneName` cases.
2. Reject negative direct indices and null or empty names in the target constructors.
3. Replace the unqualified channel bone index with a target while retaining the integer
   `ImportedAnimationChannel` constructor as the direct-index compatibility path.
4. Retain `ImportedAnimationChannel.BoneIndex` as a derived compatibility property for
   direct-index targets until `WI-C2-02` moves materialization to runtime targets. Make access on
   a named target fail explicitly.
5. Keep `W3dAnimationImporter` on the integer path.
6. Add constructor and immutability tests for both target kinds and invalid inputs.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/ImportedAnimationTarget.cs` (new)
- `src/OpenSage.Game/Content/Importing/ImportedAnimationChannel.cs`
- `src/OpenSage.Game.Tests/Content/Importing/ImportedAssetTests.cs`
- `src/OpenSage.Game.Tests/Content/Importing/W3dAnimationImporterTests.cs`

## Acceptance criteria

- [ ] Every imported channel owns exactly one validated target.
- [ ] Bone-name targets preserve the exact decoded name without normalization.
- [ ] W3D channels remain direct-index targets with unchanged keyframes.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ImportedAssetTests|FullyQualifiedName~W3dAnimationImporterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add runtime binding or hierarchy lookup.
- Do not add aliases, trimming, fuzzy matching, or bind-pose compensation.
- Do not change channel timing, value kinds, interpolation, or W3D decoding.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C2-02` to `Ready`.
- Append a result line to `.exchange`.
