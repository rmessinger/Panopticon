# Task Card `WI-C4a-06`: Select the highest NIF LOD

| Field | Value |
|---|---|
| Card ID | `WI-C4a-06` |
| Roadmap phase | [Track C · C4a](../../track-c-external-assets.md): Empire Earth II NIF 10.1 proof of concept |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4a-03` |
| Diagrams | [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) |

## Goal

Decode `NiLODNode` and `NiRangeLODData` and expose one deterministic highest-detail child for the
Bomber without adding an LOD concept to C1.

## Context to read (and only this)

- `docs/roadmap/deep-dives/ee2-gamebryo-model-storage.md` sections 7, 9, and 10.
- Completed `WI-C4a-01` Bomber LOD descriptor.
- Niftools `nif.xml` definitions for `NiSwitchNode`, `NiLODNode`, `NiLODData`, and
  `NiRangeLODData`.
- NIF scene-graph types and tests from `WI-C4a-03`.

## Steps

1. Add immutable `NifSwitchNode`, `NifLodNode`, `NifLodData`, and `NifRangeLodData` parser types.
2. Decode center and ordered near/far ranges and validate one range per LOD child.
3. Select the child whose range has the minimum near distance. Require an unambiguous,
   non-overflowing range and valid child reference.
4. Expose the selected child through a query on the parsed scene graph; retain the original
   children and ranges for diagnostics.
5. Add synthetic tests for the Bomber-shaped case, invalid counts, invalid references, and
   ambiguous ranges.

## Files likely to change

- `src/OpenSage.FileFormats.Nif/Blocks/NifLodNode.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifSwitchNode.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifLodData.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifRangeLodData.cs` (new)
- `src/OpenSage.FileFormats.Nif/NifFile.cs`
- `src/OpenSage.Game.Tests/Data/Nif/NifLodTests.cs` (new)
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs`

## Acceptance criteria

- [ ] The target inventory's Bomber ranges select the documented highest-detail child.
- [ ] Invalid or ambiguous range data fails instead of selecting by incidental block order.
- [ ] No C1 or runtime LOD type is introduced.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifLodTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement runtime distance-based LOD switching.
- Do not parse screen-space or other LOD data classes absent from the Bomber.
- Do not map nodes into engine interchange types.

## On completion

- Set Status above to `Done` or `Blocked`.
- If `WI-C4a-04` and `WI-C4a-05` are also done and `WI-C1-08` is done, change
  `WI-C4a-07` to `Ready`.
- Append a result line to `.exchange`.
