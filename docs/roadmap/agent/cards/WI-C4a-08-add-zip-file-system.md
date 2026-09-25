# Task Card `WI-C4a-08`: Add a ZIP-backed file system

| Field | Value |
|---|---|
| Card ID | `WI-C4a-08` |
| Roadmap phase | [Track C · C4a](../../track-c-external-assets.md): Empire Earth II NIF 10.1 proof of concept |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4a-01` |
| Diagrams | [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) |

## Goal

Add a read-only `ZipFileSystem` that exposes ordinary ZIP entries through the existing virtual
file-system contract used by `AssetImportContext`.

## Context to read (and only this)

- `src/Panopticon.IO/FileSystem.cs` and `FileSystemEntry.cs`.
- `src/Panopticon.IO/BigFileSystem.cs`: archive indexing and case-insensitive lookup pattern.
- `src/Panopticon.IO/VirtualFileSystem.cs`: path projection pattern.
- `test/Panopticon.Simulation.Tests/Data/DiskFileSystemTests.cs`: existing file-system test conventions.

## Steps

1. Add `ZipFileSystem` backed by `System.IO.Compression.ZipArchive` in read mode.
2. Index normalized entry paths case-insensitively and reject duplicate normalized names rather
   than choosing one by archive order.
3. Implement `GetFile` and `GetFilesInDirectory` with the same normalization, wildcard, and
   recursion semantics as the existing file systems.
4. Keep the archive alive until the file system is disposed; each `FileSystemEntry.Open` returns
   a fresh readable stream.
5. Add tests using a temporary synthetic ZIP with mixed-case paths, nested directories,
   duplicate-path rejection, repeated opens, and disposal.

## Files likely to change

- `src/Panopticon.IO/ZipFileSystem.cs` (new)
- `test/Panopticon.Simulation.Tests/Data/ZipFileSystemTests.cs` (new)

## Acceptance criteria

- [ ] ZIP lookup is normalized and case-insensitive.
- [ ] Enumeration matches existing `FileSystem` search semantics.
- [ ] Duplicate normalized paths fail explicitly.
- [ ] No EE2-specific path, asset, or parser logic enters `Panopticon.IO`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ZipFileSystemTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement ZIP write/update support or EE2 override layering.
- Do not add external compression packages.
- Do not extract archive contents to disk.

## On completion

- Set Status above to `Done` or `Blocked`.
- If `WI-C4a-07` is also done, change `WI-C4a-09` to `Ready`.
- Append a result line to `.exchange`.
