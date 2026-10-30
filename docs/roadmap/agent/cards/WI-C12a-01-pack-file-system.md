# Task Card `WI-C12a-01`: `PFHs` pack reader, LZ4, and `PackFileSystem`

| Field | Value |
|---|---|
| Card ID | `WI-C12a-01` |
| Roadmap phase | [Track C · C12a](../../track-c-external-assets.md): SHOGUN 2 packs |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | none (C1 done) |
| Diagrams | [C12 architecture diagrams](../../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams) |

## Goal

A read-only, case-insensitive `PackFileSystem` exposes every entry of the SHOGUN 2 `PFHs` packs under `data/`, with LZ4 block entries decoded on open.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 2, 9 (implementation decisions), and the class diagram.
- `src/Panopticon.IO/FileSystem.cs`, `FileSystemEntry.cs`, `BigFileSystem.cs` (directory tree and case-insensitive lookup pattern).
- `src/Panopticon.FileFormats.RefPack/` (in-repo decompressor layout).
- If they exist: `src/Panopticon.FileFormats.Lz4/` (C10a) and `src/Panopticon.FileFormats.Pack/` (C9a). Reuse them instead of creating them.

## Steps

1. If missing, add project `src/Panopticon.FileFormats.Lz4` with `Lz4BlockDecoder.Decode(ReadOnlySpan<byte>, Span<byte>)` for raw LZ4 blocks; output length must equal the destination length.
2. If missing, add project `src/Panopticon.FileFormats.Pack` (nullable enabled, references `Panopticon.FileFormats` and `Panopticon.FileFormats.Lz4`) and add both projects to `Panopticon.sln`.
3. Add `PackFile.Read(stream, path)`, `PackEntry`, and `PackVariant`: the `PFHs` 32-byte header and index from deep dive section 2, with flags `0x20` (stored size) and `0x40` (timestamp) and 16-byte aligned data. Unknown magics and pack types throw with pack path and offset. Leave a `PFH5` variant slot for C9a without implementing it.
4. Add `PackFileSystem : FileSystem` in `Panopticon.IO` over every `*.pack` under the given `data/` directory, keyed by pack paths normalized with `FileSystem.NormalizeFilePath`. Later packs in load order override earlier ones on path collisions. Raw entries open a bounded stream; compressed entries decode into a memory stream per `Open`, never per pack.
5. Add install-free tests with a synthetic pack (raw, compressed, both flags) and LZ4 vectors including literals-only, overlapping matches, and malformed input.
6. Add an Interactive test `Shogun2PackTests` that reads every pack index exactly to end of file, checks the deep dive section 2 file counts, decodes every compressed entry of `models2.pack` to its recorded size, and opens the proof `.unit_variant` and head part from section 7.

## Files likely to change

- `src/Panopticon.FileFormats.Lz4/` (new, unless C10a landed): `Lz4BlockDecoder.cs`
- `src/Panopticon.FileFormats.Pack/` (new, unless C9a landed): `PackFile.cs`, `PackEntry.cs`, `PackVariant.cs`
- `src/Panopticon.IO/PackFileSystem.cs` (new), `Panopticon.IO.csproj`, `Panopticon.sln`
- `test/Panopticon.Simulation.Tests/Content/Importing/Shogun2/` (new tests), test `.csproj` reference

## Acceptance criteria

- [ ] Synthetic pack tests pass for raw, compressed, and rejected entries.
- [ ] LZ4 decoder handles literals-only, overlapping matches, and malformed input (throws, never reads out of bounds).
- [ ] Interactive: every pack index is consumed exactly; counts match deep dive section 2 or the deep dive is corrected.
- [ ] Lookups are case-insensitive and accept `/` or `\`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
SHOGUN2_PATH="/mnt/pit2/Network Storage/Transfer/Shogun 2 Complete Edition" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Shogun2"
```

## Out of scope / guardrails

- DB tables (`WI-C12a-02`) and any model format.
- The C9 `PFH5` layout.
- No new NuGet package.
- No SHOGUN 2 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C12a-02`, `WI-C12b-01`, and `WI-C12c-01` to `Ready`.
- If this card created `Panopticon.FileFormats.Lz4`, note it in `WI-C10a-01` so C10a reuses it.
- Append a result line to `.exchange`.
