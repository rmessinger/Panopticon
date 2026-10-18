# Task Card `WI-C8a-04`: Add the SAGE W3D corpus manifest and installed corpus test

| Field | Value |
|---|---|
| Card ID | `WI-C8a-04` |
| Roadmap phase | [Track C · C8a](../../track-c-external-assets.md): SAGE-family W3D corpus |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `M` |
| Prerequisites | `WI-C8a-03` |
| Diagrams | [C8 architecture diagrams](../../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams) |

## Goal

Parse and convert every `.w3d` in Generals, Zero Hour, BFME, BFME2, and RotWK in installed tests, and assert the failure set equals a checked-in manifest of file names and reason codes.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` sections 2, 4, 4.1, and the C8a sequence diagram.
- `test/Panopticon.Simulation.Tests/Data/InstalledFilesTestData.cs`, `GameFactAttribute.cs`.
- `test/Panopticon.Simulation.Tests/Data/W3d/W3dFileTests.cs`: inline skip list.
- `src/Panopticon.Simulation/Data/InstallationLocator.cs`: `GameInstallation.CreateFileSystem` base-game layering.

## Steps

1. Add `test/Panopticon.Simulation.Tests/Data/W3d/SageW3dCorpus.json`: per game (`CncGenerals`, `CncGeneralsZeroHour`, `Bfme`, `Bfme2`, `Bfme2Rotwk`), lists `parseFailures`, `conversionFailures`, and `roundtripExclusions`, each entry `{ "path", "reason" }`. Names and reasons only, no asset bytes. Reasons: `corrupt-animation`, `unknown-mapping-type`, `decode-error`, `not-w3d`, `shdmesh-render-object`, `shader-family`, `multi-shader-material`, `ws-classic-env`, `roundtrip-only`.
2. Seed it from the section 4 list and the 26 inline names in `CanRoundtripW3dFiles`, then replace the inline `switch` with a manifest lookup.
3. Add `SageW3dCorpusTests` with one `[GameFact(game)]` per game. Each enumerates `.w3d` files under its own install root only, while resolving references through `installation.CreateFileSystem()` (RotWK over BFME2, Zero Hour over Generals). Each file: `W3dFile.FromStream`; then `W3dModelImporter.Convert` when the file has render objects or an `HLod`, otherwise `W3dAnimationImporter.Convert` when it has an animation. Use the game's resolver from its `CreateAssetLoadStrategy()`.
4. Record failures as `(path, reason)`; assert set equality with the manifest and print unexpected and missing entries on failure.
5. Run with all five installs. Record observed conversion failures in the manifest. The expected set is section 4.1 (shader families, multi-id, `WsClassicEnv`) plus the three `SHDMESH` wall models; anything else stops the card for review.
6. Print per-game totals (files, parsed, converted) to test output and copy them into `.exchange`.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Data/W3d/SageW3dCorpus.json`, `SageW3dCorpusManifest.cs`, `SageW3dCorpusTests.cs` (new)
- `test/Panopticon.Simulation.Tests/Data/W3d/W3dFileTests.cs`
- `test/Panopticon.Simulation.Tests/Panopticon.Simulation.Tests.csproj` (copy the manifest to output)

## Acceptance criteria

- [ ] Each game's failures equal its manifest entries.
- [ ] Generals has no entries; Zero Hour lists only the five `UISabotr_*` animations.
- [ ] Without the install env vars the tests are skipped, not failed.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
CNC_GENERALS_ZH_PATH="/path/to/Command & Conquer Generals - Zero Hour" \
BFME_PATH="/path/to/BFME" BFME2_PATH="/path/to/BFME2" BFME2_ROTWK_PATH="/path/to/BFME2XP" \
  dotnet test src --filter "FullyQualifiedName~SageW3dCorpus|FullyQualifiedName~W3dFileTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not fix conversion hazards here. C8b removes their manifest entries.
- Do not commit game files or extracted bytes.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C8a-01` is done, tick C8a in `docs/roadmap/implementation-checklist.md` and set C8a `Done` in the track doc.
- If done, change `WI-C8b-01`, `WI-C8c-01`, `WI-C8c-02`, and `WI-C8c-03` to `Ready`.
- Append a result line to `.exchange`.
