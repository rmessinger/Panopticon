# Task Card `WI-C8a-03`: Fail soft in the W3D importers

| Field | Value |
|---|---|
| Card ID | `WI-C8a-03` |
| Roadmap phase | [Track C · C8a](../../track-c-external-assets.md): SAGE-family W3D corpus |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C8a-02` |
| Diagrams | [C8 architecture diagrams](../../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams) |

## Goal

Turn a W3D file that fails to parse into one logged diagnostic and a null asset, and log the parse diagnostics of files that do parse.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` section 6.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dModelImporter.cs`: `FindAndParseFile`.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dAnimationImporter.cs`: `ImportAnimation`.
- `src/Panopticon.Simulation/Content/Loaders/OnDemandModelLoader.cs`: null handling.

## Steps

1. Add one internal helper, for example `W3dFileReader.TryRead(FileSystemEntry entry, out W3dFile? file)`, that calls `W3dFile.FromStream` and catches `InvalidDataException`, `EndOfStreamException`, and `NotSupportedException` thrown during parsing. On failure it logs a warning `W3D100 <path>: <exception message>` and returns false.
2. On success it logs each `W3dFile.Diagnostics` entry as a warning prefixed with the file path.
3. Use the helper in `W3dModelImporter.FindAndParseFile` (both model and skeleton paths) and in `W3dAnimationImporter.ImportAnimation`. Both already return null for missing files; a failed parse takes the same branch.
4. Conversion keeps throwing contextual exceptions (C1 contract). Do not catch in `Convert`.
5. Tests: a truncated synthetic model and a truncated animation return null from the importers; a file with an unknown top-level chunk imports and the diagnostic is observable through a test log target or a returned diagnostics list.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/W3d/W3dFileReader.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dModelImporter.cs`, `W3dAnimationImporter.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/W3dModelImporterTests.cs`, `W3dAnimationImporterTests.cs`

## Acceptance criteria

- [ ] Step 5 tests pass.
- [ ] `W3dImportParityTests` pass unchanged with `CNC_GENERALS_PATH` set.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~W3d"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not swallow conversion or materialization exceptions.
- If a determinism or golden-baseline test fails, stop and report per `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C8a-04` to `Ready`.
- Append a result line to `.exchange`.
