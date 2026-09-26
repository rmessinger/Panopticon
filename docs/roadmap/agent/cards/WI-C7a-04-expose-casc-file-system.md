# Task Card `WI-C7a-04`: Expose CASC storage as a file system

| Field | Value |
|---|---|
| Card ID | `WI-C7a-04` |
| Roadmap phase | [Track C · C7a](../../track-c-external-assets.md): StarCraft II CASC storage |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C7a-02`, `WI-C7a-03` |
| Diagrams | [C7a/C7b architecture diagrams](../../deep-dives/sc2-casc-m3-storage.md#14-architecture-diagrams) |

## Goal

Compose the readers into `CascStorage` and expose it as `Panopticon.IO.CascFileSystem`, verified against the operator install.

## Context to read (and only this)

- `docs/roadmap/deep-dives/sc2-casc-m3-storage.md` sections 3.1, 4, 11, and 13.
- `src/Panopticon.IO/FileSystem.cs`, `src/Panopticon.IO/ZipFileSystem.cs`: API, path normalization, and directory listing to mirror.
- `test/Panopticon.Simulation.Tests/Data/ZipFileSystemTests.cs`.
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifImportTests.cs:15-35`: env-var gate pattern.

## Steps

1. Add `CascStorage.Open(installRoot)`: build info, index, encoding (read by encoding key), root (content key through encoding), then `MndxRoot` entries. Expose `Entries` and `OpenFile(CascKey contentKey)`.
2. Reference `Panopticon.FileFormats.Casc` from `Panopticon.IO`. Add `CascFileSystem : FileSystem` with a case-insensitive `/` path index and a directory index like `ZipFileSystem`. `GetFile` returns an entry whose `Open()` returns a fresh `MemoryStream`. Dispose closes archives.
3. Synthetic test: assemble a minimal install directory from the earlier test helpers (build info, one `.idx`, one `data.000`, encoding, MNDX root) and read two files through `CascFileSystem` with mixed-case paths and directory listing.
4. Add `Sc2CascInstallTests` (`Category=Interactive`, requires `STARCRAFT_II_PATH`): enumerate 675,745 entries, open the three proof models with mixed-case paths, and assert their byte sizes and MD5 content keys from the deep dive. Assert `.m3`, `.m3a`, and `.dds` counts from section 4.

## Files likely to change

- `src/Panopticon.FileFormats.Casc/CascStorage.cs` (new)
- `src/Panopticon.IO/CascFileSystem.cs` (new), `src/Panopticon.IO/Panopticon.IO.csproj`
- `test/Panopticon.Simulation.Tests/Data/Casc/CascFileSystemTests.cs`, `Sc2CascInstallTests.cs` (new)

## Acceptance criteria

- [ ] The synthetic install reads both files case-insensitively and lists directories.
- [ ] Installed: 675,745 entries, the section 4 extension counts, and the proof models' sizes and MD5 values match.
- [ ] A missing `STARCRAFT_II_PATH` fails the installed tests with a clear message and does not affect non-interactive runs.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Casc"
STARCRAFT_II_PATH="/path/to/StarCraft II" \
  dotnet test src --filter "FullyQualifiedName~Sc2Casc"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not register CASC into `GameInstallation` or any game file system. SC2 roots stay separate.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, set C7a Status to `Done` in the track doc, tick the checklist item, and change `WI-C7b-01` to `Ready`.
- Append a result line to `.exchange`.
