# Task Card `WI-C4-02`: Resolve glTF resources

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C4-02` |
| Roadmap phase | [Track C · C4](../../track-c-external-assets.md): First production external format |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C1-14`, `WI-C4-01` |
| Diagrams | [C4 architecture diagrams](../../deep-dives/c4-gltf-importer.md#11-architecture-diagrams) |

## Goal

Resolve every supported glTF buffer and image source through the import context's virtual file
system without traversal, ambient disk access, or repeated reads.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4-gltf-importer.md` sections 2, 3, and 9.
- `src/OpenSage.IO/FileSystem.cs`.
- `src/OpenSage.IO/FileSystemEntry.cs`.
- `src/OpenSage.IO/VirtualFileSystem.cs`: virtual-root behavior.
- `src/OpenSage.Game/Content/Importing/AssetImportContext.cs`.
- `src/OpenSage.Game.Tests/Content/DataOnlyAssetLoadingTests.cs`: `MemoryFileSystem`.
- `src/OpenSage.Game/Content/Importing/Gltf/GltfDocumentLoader.cs` (new from `WI-C4-01`).
- `src/OpenSage.Game/Content/Importing/Gltf/GltfImportOptions.cs` (new from `WI-C4-01`).

## Steps

1. Add `GltfResourceResolver` scoped to one normalized root-document path and one
   `AssetImportContext.FileSystem`.
2. Resolve relative URI paths against the document's virtual directory. Normalize separators,
   reject rooted paths, non-`data` schemes, encoded separators, and any `..` traversal that would
   leave the virtual root before calling `GetFile`.
3. Open external buffers and images through `FileSystemEntry`, enforce per-resource and aggregate
   byte limits before allocation, and cache one immutable byte payload per normalized path.
4. Decode bounded base64 and percent-encoded data URIs in memory. Reject malformed media types,
   malformed escapes, and oversized payloads as invalid data.
5. Supply SharpGLTF's actual read callback API for external resources while leaving GLB chunks in
   the root document stream. Do not hand-parse GLB chunks.
6. Expose the exact normalized external image path separately from copied buffer bytes so card 9
   can create the C3 path-backed image source.
7. Add tests for `.gltf`, `.glb`, external, data-URI, and repeated resources, plus absolute,
   scheme, encoded-separator, traversal, missing-reference, and aggregate-limit failures.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/Gltf/GltfResourceResolver.cs` (new)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfDocumentLoader.cs` (new from `WI-C4-01`)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfImportOptions.cs` (new from `WI-C4-01`)
- `src/OpenSage.Game.Tests/Content/Importing/GltfResourceResolverTests.cs` (new)

## Acceptance criteria

- [ ] All external reads use `AssetImportContext.FileSystem`.
- [ ] Paths cannot escape the virtual root or select an ambient disk location.
- [ ] External, data-URI, and GLB-backed resources are supported within configured limits.
- [ ] Repeated normalized external references are read once per import.
- [ ] Missing referenced resources throw contextual `InvalidDataException`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GltfResourceResolverTests|FullyQualifiedName~GltfImporterScaffoldTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not fall back to `System.IO.File`, `File.Open`, or current-directory lookup.
- Do not retain `AssetImportContext` or `FileSystem` in a process-global cache.
- Do not decode image pixels or map materials.
- Do not accept network URLs, `file:` URIs, or paths above the virtual root.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4-03` is done, change `WI-C4-05` to `Ready`.
- Append a result line to `.exchange`.
