# Task Card `WI-C10a-01`: BAR archive reader, LZ4, and `BarFileSystem`

| Field | Value |
|---|---|
| Card ID | `WI-C10a-01` |
| Roadmap phase | [Track C · C10a](../../track-c-external-assets.md): AoE3 DE archives and textures |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | none (C1 done) |
| Diagrams | [C10 architecture diagrams](../../deep-dives/c10-aoe3de-granny-models.md#9-architecture-diagrams) |

## Goal

A read-only, case-insensitive `BarFileSystem` exposes every entry of the `.bar` v6 archives under an AoE3 DE `Game/` directory, with `alz4` entries decompressed.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` sections 1, 2, 7 (implementation decisions), and the class diagram.
- `src/Panopticon.IO/FileSystem.cs`, `FileSystemEntry.cs`, `BigFileSystem.cs` (directory tree and case-insensitive lookup pattern).
- `src/Panopticon.FileFormats.RefPack/` (in-repo decompressor layout).

## Steps

1. Add project `src/Panopticon.FileFormats.Bar` (nullable enabled, references `Panopticon.FileFormats` and `Panopticon.FileFormats.Lz4`), add it to `Panopticon.sln`, and reference it from `Panopticon.IO`.
2. Add `BarArchive.Read(stream)` and `BarEntry`: `ESPN` magic, version 6, `0x44332211`, checksum, file count, u64 table offset at `0x120`; per entry offset, decompressed, stored, and padded sizes, UTF-16LE path, compression flag. Unknown versions and magics throw with path and offset.
3. Add `Lz4BlockDecoder.Decode(ReadOnlySpan<byte>, Span<byte>)` (raw LZ4 block format) in a new `src/Panopticon.FileFormats.Lz4` project, unless `WI-C12a-01` already created it, and the `alz4` wrapper (magic, decompressed size, compressed size, version 1). Output length must equal the recorded size.
4. Add `BarFileSystem : FileSystem` over every `*.bar` under the given directory, keyed by `Art\...`-style relative paths normalized with `FileSystem.NormalizeFilePath`. Flag 0 opens a bounded stream; flag 1 decodes `alz4` into a memory stream; flag 2 (encrypted, `Sound.bar`) and unknown flags throw with archive and entry path on `Open`.
5. Add install-free tests with a synthetic archive (stored, `alz4`, encrypted entries) and LZ4 vectors including overlapping matches.
6. Add an Interactive test `Aoe3BarArchiveTests` that reads all 42 tables exactly to end of file and opens `Art\units\infantry_ranged\musketeer\musketeer_4age.gr2` at its recorded size.

## Files likely to change

- `src/Panopticon.FileFormats.Bar/` (new: `BarArchive.cs`, `BarEntry.cs`, `BarCompression.cs`, `Alz4.cs`)
- `src/Panopticon.FileFormats.Lz4/Lz4BlockDecoder.cs` (new, unless `WI-C12a-01` landed)
- `src/Panopticon.IO/BarFileSystem.cs` (new), `Panopticon.IO.csproj`, `Panopticon.sln`
- `test/Panopticon.Simulation.Tests/Content/Importing/Aoe3/` (new tests), test `.csproj` reference

## Acceptance criteria

- [ ] Synthetic archive tests pass for stored, `alz4`, and rejected entries.
- [ ] LZ4 decoder handles literals-only, overlapping matches, and malformed input (throws, never reads out of bounds).
- [ ] Interactive: 42 archives, every table consumed exactly; entry counts recorded in the C10 deep dive.
- [ ] Lookups are case-insensitive and accept `/` or `\`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
AOE3DE_PATH="/mnt/pit2/Network Storage/Transfer/AoE3DE" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Aoe3"
```

## Out of scope / guardrails

- XMB, DDT, and Granny parsing (`WI-C10a-02`, `WI-C10a-03`, `WI-C10b-01`).
- Sound decryption.
- No new NuGet package.
- No AoE3 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C10a-02`, `WI-C10a-03`, and `WI-C10b-01` to `Ready`.
- Tick C10a in the checklist when its last card is done, and update the README progress row.
- Append a result line to `.exchange`.
