# Task Card `WI-C4a-09`: Verify the EE2 import descriptors

| Field | Value |
|---|---|
| Card ID | `WI-C4a-09` |
| Roadmap phase | [Track C · C4a](../../track-c-external-assets.md): Empire Earth II NIF 10.1 proof of concept |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4a-07`, `WI-C4a-08` |
| Diagrams | [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) |

## Goal

Lock the two installed EE2 target models to the complete CPU descriptors recorded by
`WI-C4a-01`.

## Context to read (and only this)

- Completed target descriptors in
  `docs/roadmap/deep-dives/ee2-gamebryo-model-storage.md` section 9.
- `src/OpenSage.Game.Tests/Data/W3d/W3dFileTests.cs`: installed-asset assertion pattern.
- `src/OpenSage.Game.Tests/Content/Importing/NifModelImporterTests.cs`.
- `src/OpenSage.IO/ZipFileSystem.cs`.

## Steps

1. Add interactive tests that require
   `EMPIRE_EARTH_II_PATH/zips/graphics.zip` and fail with a direct setup message when the variable
   or archive is absent.
2. Import the exact archive paths for the War Galley and Twin-Engine Bomber through
   `ZipFileSystem` and `NifModelImporter`.
3. Assert model name, skeleton/node order, sub-object names and parent indices, vertex and triangle
   counts, bounds, material state, and source texture names against the completed inventory.
4. Assert no imported mesh is skinned and the Bomber descriptor contains only the selected
   highest-detail LOD geometry.
5. Mark both tests `Category=Interactive` so routine install-free validation excludes them.

## Files likely to change

- `src/OpenSage.Game.Tests/Content/Importing/Ee2NifImportTests.cs` (new)

## Acceptance criteria

- [ ] Both installed assets match every descriptor recorded by `WI-C4a-01`.
- [ ] The Bomber test proves lower-detail LOD children are absent.
- [ ] Missing local prerequisites produce an explicit setup failure when the interactive test is
      selected.
- [ ] No EE2 asset, extracted data, or generated golden binary is committed.
- [ ] Standard validation passes.

## Validation commands

```sh
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2NifImportTests"
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not relax expected descriptors when an unexplained installed-asset difference appears.
- Do not inspect animations, textures, damage variants, patch archives, or DDF records.
- Do not copy local assets into test data.

## On completion

- Set Status above to `Done` or `Blocked`.
- If `WI-C1-14` and `WI-C4a-10` are done, change `WI-C4a-11` to `Ready`.
- Append a result line to `.exchange`.
