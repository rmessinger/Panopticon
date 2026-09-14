# Task Card `WI-C4a-03`: Decode the NIF scene graph

| Field | Value |
|---|---|
| Card ID | `WI-C4a-03` |
| Roadmap phase | [Track C · C4a](../../track-c-external-assets.md): Empire Earth II NIF 10.1 proof of concept |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4a-02` |
| Diagrams | [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) |

## Goal

Decode the NIF 10.1 base object fields and `NiNode` hierarchy needed to retain names, local
transforms, child references, property references, and extra-data references.

## Context to read (and only this)

- `docs/roadmap/deep-dives/ee2-gamebryo-model-storage.md` sections 3, 9, and 10.
- Completed `WI-C4a-01` target descriptors.
- Niftools `nif.xml` definitions for `NiObjectNET`, `NiAVObject`, `NiNode`, and
  `NiStringExtraData`.
- NIF types and synthetic builder added by `WI-C4a-02`.

## Steps

1. Add one immutable type per decoded block and retain block indices as references until the full
   file graph has been read.
2. Decode `NiObjectNET`, `NiAVObject`, `NiNode`, and passive `NiStringExtraData` fields for version
   10.1.0.0.
3. Add `NifFile.Read` block dispatch for those types. Unknown block types throw
   `NotSupportedException` naming the block and index.
4. Validate every object, child, property, and extra-data reference after block decoding. Reject
   out-of-range references, cycles, duplicate parents, and roots that cannot form a forest.
5. Add synthetic tests for one nested node graph and each invalid-reference case.

## Files likely to change

- `src/OpenSage.FileFormats.Nif/NifFile.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifObjectNet.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifAvObject.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifNode.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifStringExtraData.cs` (new)
- `src/OpenSage.Game.Tests/Data/Nif/NifSceneGraphTests.cs` (new)
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs`

## Acceptance criteria

- [ ] A nested synthetic node graph round-trips names, transforms, and ordered references.
- [ ] Invalid references, cycles, duplicate parents, and unsupported blocks fail contextually.
- [ ] Block types remain parser values and do not reference C1 or runtime graphics types.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifSceneGraphTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not decode geometry, render properties, LOD, skinning, controllers, or animation.
- Do not flatten transforms into engine interchange types.

## On completion

- Set Status above to `Done` or `Blocked`.
- Change `WI-C4a-04`, `WI-C4a-05`, and `WI-C4a-06` to `Ready`.
- Append a result line to `.exchange`.
