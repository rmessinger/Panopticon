# Task Card `WI-C10b-01`: Generic Granny container reader

| Field | Value |
|---|---|
| Card ID | `WI-C10b-01` |
| Roadmap phase | [Track C · C10b](../../track-c-external-assets.md): AoE3 DE static Granny models |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-C10a-01` (corpus access) |
| Diagrams | [C10 architecture diagrams](../../deep-dives/c10-aoe3de-granny-models.md#9-architecture-diagrams) |

## Goal

`GrannyFile.Read(stream)` loads uncompressed Granny version 7 files into a generic object graph driven by the embedded type trees, and rejects compressed sections explicitly.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` sections 1, 3, 6, 7, and the class diagram.
- LSLib's Granny reader (MIT) as a format reference only, with credit in the file header; do not port its native decompressor.

## Steps

1. Add project `src/Panopticon.FileFormats.Granny` and add it to `Panopticon.sln`; reference it from `Panopticon.Simulation`.
2. Header: 64-bit little-endian magic, file format version 7, section table, root type and root object references. Other magics or versions throw with the values found.
3. Sections: read each section; compression 0 is raw; any other value throws `GrannyCompressedSectionException` with file path, section index, and compression type (Oodle1 2, BitKnit2 4).
4. Apply pointer relocations and mixed-marshalling fixups per section.
5. Type tree: member type, name, referenced type, array size, and extra fields; build `GrannyObject` values with named fields, inline arrays, references, reference arrays, variant references, and strings.
6. Install-free tests from a hand-built minimal file (one struct, one array, one reference); Interactive corpus test over `ArtUnits.bar` reporting uncompressed, compressed, and failed counts by reason.

## Files likely to change

- `src/Panopticon.FileFormats.Granny/` (new: `GrannyFile.cs`, `GrannyHeader.cs`, `GrannySection.cs`, `GrannyTypeTree.cs`, `GrannyObject.cs`, `GrannyCompressedSectionException.cs`)
- `Panopticon.sln`, `src/Panopticon.Simulation/Panopticon.Simulation.csproj`
- `test/Panopticon.Simulation.Tests/Content/Importing/Aoe3/GrannyFileTests.cs` (new)

## Acceptance criteria

- [ ] Synthetic file parses to the expected object graph.
- [ ] Interactive: every uncompressed `.gr2` in `ArtUnits.bar` parses; compressed files fail only with `GrannyCompressedSectionException`; counts recorded against the section 3 sample (about 85% uncompressed).
- [ ] Musketeer root object exposes `ArtToolInfo`, `Skeletons`, `Meshes`, and `Models` by name.
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

- Mesh, skeleton, or animation conversion (`WI-C10b-02`, `WI-C10c-02`).
- Section decompression (C10d).
- No AoE3 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C10b-02` and `WI-C10c-02` to `Ready`.
- Tick C10b in the checklist when its last card is done, and update the README progress row.
- Append a result line to `.exchange`.
