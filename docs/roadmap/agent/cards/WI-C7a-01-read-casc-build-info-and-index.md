# Task Card `WI-C7a-01`: Read CASC build info and index files

| Field | Value |
|---|---|
| Card ID | `WI-C7a-01` |
| Roadmap phase | [Track C · C7a](../../track-c-external-assets.md): StarCraft II CASC storage |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [C7a/C7b architecture diagrams](../../deep-dives/sc2-casc-m3-storage.md#14-architecture-diagrams) |

## Goal

Create `Panopticon.FileFormats.Casc` with `CascBuildInfo` (`.build.info` and build config) and `CascIndex` (version 7 `.idx` buckets), covered by synthetic tests.

## Context to read (and only this)

- `docs/roadmap/deep-dives/sc2-casc-m3-storage.md` sections 3.1 to 3.3 and 11.
- `src/Panopticon.FileFormats.Nif/Panopticon.FileFormats.Nif.csproj`: project shape to copy.
- `src/Panopticon.sln`: project registration.
- `test/Panopticon.Simulation.Tests/Panopticon.Simulation.Tests.csproj`: test project references.

## Steps

1. Create `src/Panopticon.FileFormats.Casc/Panopticon.FileFormats.Casc.csproj` like the NIF project (no dependencies) and add it to `src/Panopticon.sln` and the test project.
2. Add `CascKey` (16-byte value type with hex parse, equality, and a 9-byte prefix accessor).
3. Add `CascBuildInfo.Read(installRoot)`: parse the pipe table header `Name!TYPE:size`, select the row with `Active` = 1, read `Build Key`, open `SC2Data/config/<k[0..2]>/<k[2..4]>/<key>`, parse `key = value` lines, and expose `RootContentKey`, `EncodingContentKey`, `EncodingEncodingKey`, and `BuildUid`.
4. Add `CascIndex.Load(dataDirectory)`: pick the highest version file per bucket `00`..`0F`, validate the section 3.3 header (version 7, widths 4, 5, 9, 30), and read 18-byte entries into a dictionary keyed by 9-byte prefix, first entry winning. Expose `TryGetSpan(CascKey, out CascSpan)` with archive, offset, and size.
5. Write synthetic tests that build a temporary install directory with a two-row `.build.info`, a build config, and two `.idx` files (one bucket with two versions). Assert the active row, the parsed keys, the highest-version pick, first-wins duplicates, and span decoding of a 5-byte big-endian offset.
6. Test failures: missing `.build.info`, no active row, `.idx` version 6 (`NotSupportedException`).

## Files likely to change

- `src/Panopticon.FileFormats.Casc/*` (new)
- `src/Panopticon.sln`
- `test/Panopticon.Simulation.Tests/Panopticon.Simulation.Tests.csproj`
- `test/Panopticon.Simulation.Tests/Data/Casc/CascBuildInfoTests.cs`, `CascIndexTests.cs` (new)

## Acceptance criteria

- [ ] Synthetic `.build.info` and config parse to the expected keys.
- [ ] The highest `.idx` version per bucket is used and duplicates resolve first-wins.
- [ ] Offset `0x0040000010` decodes to archive 1, offset 16.
- [ ] Missing files and unsupported index versions fail with the section 11 exception types.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not read `data.NNN`, BLTE, encoding, or root files. Those are `WI-C7a-02` and `WI-C7a-03`.
- No native CascLib or P/Invoke.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C7a-02` to `Ready`.
- Append a result line to `.exchange`.
