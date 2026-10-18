# Task Card `WI-C8a-02`: Skip unknown W3D chunks with diagnostics

| Field | Value |
|---|---|
| Card ID | `WI-C8a-02` |
| Roadmap phase | [Track C · C8a](../../track-c-external-assets.md): SAGE-family W3D corpus |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | none |
| Diagrams | [C8 architecture diagrams](../../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams) |

## Goal

Replace the throw on unknown chunk ids with a skip that records a `W3dParseDiagnostic`, and keep top-level unknown chunks as raw bytes so files containing them still roundtrip byte-exactly.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` sections 4, 6, and the appendix.
- `src/Panopticon.FileFormats.W3d/W3dChunk.cs`: `ParseChunk`, `ParseChunks`, `CreateUnknownChunkException`, `W3dListContainerChunk.ParseItems`.
- `src/Panopticon.FileFormats.W3d/W3dFile.cs`, `W3dParseContext.cs`, `W3dChunkHeader.cs`.
- `test/Panopticon.Simulation.Tests/Data/W3d/W3dFileTests.cs`.

## Steps

1. Add `public sealed record W3dParseDiagnostic(string Code, string ChunkPath, long Offset, string Message)` and `W3dParseContext.AddDiagnostic` / `Diagnostics`. `ChunkPath` joins the context stack names, for example `W3dFile/W3dMesh`.
2. Add `W3dContainerChunk.SkipUnknownChunk(reader, context, chunkType)`: read the raw size word, record diagnostic `W3D001` (`Unknown chunk 0x0B00 (2816 bytes)`), seek past the payload, and return the raw size word and bytes.
3. Add `public sealed record W3dUnknownChunk(uint RawChunkType, uint RawSizeWord, byte[] Data)` that writes back the type, the original size word, and the bytes. Do not derive the size word from `W3dChunkHeader.HasSubChunks`, which is always false today.
4. In `W3dFile.Parse`, the default case adds a `W3dUnknownChunk` to `Chunks` (not to `RenderableObjects`). Add `W3dFile.Diagnostics`.
5. Replace every other `throw CreateUnknownChunkException(...)` site (23 files) with `SkipUnknownChunk`; nested unknown chunks are not preserved. Remove `CreateUnknownChunkException` when unused.
6. Size mismatches in `ParseChunk` keep throwing `InvalidDataException`.
7. Tests: a synthetic file with a top-level `0x0B00` chunk between a mesh and an `HLod` parses, reports one `W3D001`, and roundtrips byte-exactly; a synthetic mesh with an unknown nested chunk parses with one diagnostic; a truncated chunk still throws.

## Files likely to change

- `src/Panopticon.FileFormats.W3d/W3dChunk.cs`, `W3dFile.cs`, `W3dParseContext.cs`
- `src/Panopticon.FileFormats.W3d/W3dParseDiagnostic.cs`, `W3dUnknownChunk.cs` (new)
- The 23 files that call `CreateUnknownChunkException`
- `test/Panopticon.Simulation.Tests/Data/W3d/W3dUnknownChunkTests.cs` (new)

## Acceptance criteria

- [ ] Synthetic tests in step 7 pass.
- [ ] `CanRoundtripW3dFiles` and the existing W3D importer tests pass unchanged with `CNC_GENERALS_PATH` set.
- [ ] No unknown-chunk throw remains in `Panopticon.FileFormats.W3d`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~W3d"
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~W3d"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not decode `SHDMESH`. That is `WI-C8b-04`.
- Do not change importer behavior. That is `WI-C8a-03`.
- Do not fix `W3dChunkHeader.HasSubChunks`; record it in `.exchange` if it blocks a test.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C8a-03` and `WI-C8b-04` to `Ready`.
- Append a result line to `.exchange`.
