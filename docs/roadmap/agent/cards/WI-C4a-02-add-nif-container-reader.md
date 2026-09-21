# Task Card `WI-C4a-02`: Add the NIF 10.1 container reader

| Field | Value |
|---|---|
| Card ID | `WI-C4a-02` |
| Roadmap phase | [Track C · C4a](../../track-c-external-assets.md): Empire Earth II NIF 10.1 proof of concept |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4a-01` |
| Diagrams | [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) |

## Goal

Add a NIF file-format project with bounded binary primitives and header/block-directory parsing
for Gamebryo 10.1.0.0, without decoding scene blocks yet.

## Context to read (and only this)

- `docs/roadmap/deep-dives/ee2-gamebryo-model-storage.md` sections 3, 9, and 10.
- `src/OpenSage.FileFormats.W3d/OpenSage.FileFormats.W3d.csproj`: file-format project pattern.
- `src/OpenSage.FileFormats.W3d/W3dFile.cs`: stream-reader and validation pattern.
- `src/OpenSage.Game.Tests/Data/W3d/W3dFileTests.cs`: parser-test pattern.

## Steps

1. Add `OpenSage.FileFormats.Nif` to the solution, reference the shared file-format and mathematics
   projects, and reference it from `OpenSage.Game.Tests`.
2. Add a little-endian `NifBinaryReader` for bounded integers, floats, vectors, matrices, sized
   strings, and block references. Reject counts that exceed remaining stream data before
   allocating.
3. Add immutable `NifHeader` and block-directory entries. Accept only header text
   `Gamebryo File Format, Version 10.1.0.0`, version `0x0A010000`, user version 0, and zero groups.
4. Preserve block type order and each block's type index. Reject out-of-range type indices and
   malformed strings with `InvalidDataException`; reject other valid versions with
   `NotSupportedException`.
5. Add synthetic tests for the accepted header and each failure case. Build test bytes in code;
   do not add copied EE2 bytes.

## Files likely to change

- `src/OpenSage.FileFormats.Nif/OpenSage.FileFormats.Nif.csproj` (new)
- `src/OpenSage.FileFormats.Nif/NifBinaryReader.cs` (new)
- `src/OpenSage.FileFormats.Nif/NifHeader.cs` (new)
- `src/OpenSage.FileFormats.Nif/NifBlockDirectoryEntry.cs` (new)
- `src/OpenSage.sln`
- `src/OpenSage.Game.Tests/OpenSage.Game.Tests.csproj`
- `src/OpenSage.Game.Tests/Data/Nif/NifHeaderTests.cs` (new)
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs` (new)

## Acceptance criteria

- [ ] Synthetic valid 10.1.0.0 headers preserve type names and per-block type indices.
- [ ] Invalid lengths, counts, type indices, versions, user versions, and group counts fail with
      the specified exception type.
- [ ] The reader performs no graphics, `AssetStore`, ZIP, or C1 interchange work.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifHeaderTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not decode block bodies.
- Do not add a NuGet NIF parser or generated nif.xml object model.
- Do not add animation or texture-container support.

## On completion

- Set Status above to `Done` or `Blocked`.
- Change `WI-C4a-03` to `Ready`.
- Append a result line to `.exchange`.
