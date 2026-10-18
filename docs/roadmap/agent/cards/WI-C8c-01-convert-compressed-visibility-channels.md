# Task Card `WI-C8c-01`: Convert compressed visibility bit channels

| Field | Value |
|---|---|
| Card ID | `WI-C8c-01` |
| Roadmap phase | [Track C · C8c](../../track-c-external-assets.md): BFME-family skinning and animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C8a-04` |
| Diagrams | [C8 architecture diagrams](../../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams) |

## Goal

Import `W3dCompressedAnimation.TimeCodedBitChannels` as visibility channels instead of dropping them.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` sections 4.1 and 8.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dAnimationImporter.cs`: `ConvertBitChannel` (uncompressed reference) and `ConvertCompressed`.
- `src/Panopticon.FileFormats.W3d/W3dTimeCodedBitChannel.cs`: `W3dTimeCodedBitDatum` and its `TODO: Verify` note.

## Steps

1. In `ConvertCompressed`, convert each `TimeCodedBitChannels` entry with `ChannelType == Visibility` into an `ImportedAnimationChannel` of type `Visibility`: a keyframe at time 0 with `DefaultValue` when the first time code is not 0, then one keyframe per datum at `TimeCode / FrameRate`. Other bit channel types throw `NotSupportedException` like `ConvertBitChannel`.
2. Verify the bit-31 value guess: find installed clips that ship both an uncompressed and a compressed form of the same animation, or compare against the clip's mesh visibility in captures. Record the result in `.exchange`; if the guess is wrong, fix `W3dTimeCodedBitDatum.Parse` and its writer together.
3. Synthetic test: a compressed animation with one visibility bit channel imports with the expected keyframes and times.
4. Extend `SageW3dCorpusTests` (or a sibling installed test) to count imported compressed visibility channels per game and assert they are nonzero for BFME and BFME2.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/W3d/W3dAnimationImporter.cs`
- `src/Panopticon.FileFormats.W3d/W3dTimeCodedBitChannel.cs` (only if step 2 finds the guess wrong)
- `test/Panopticon.Simulation.Tests/Content/Importing/W3dAnimationImporterTests.cs`, `test/Panopticon.Simulation.Tests/Data/W3d/SageW3dCorpusTests.cs`

## Acceptance criteria

- [ ] Step 3 and step 4 tests pass.
- [ ] Generals animation import output is unchanged: `W3dImportParityTests` pass with `CNC_GENERALS_PATH` set. Generals clips carrying compressed bit channels, if any, are listed in `.exchange`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
BFME_PATH="/path/to/BFME" BFME2_PATH="/path/to/BFME2" BFME2_ROTWK_PATH="/path/to/BFME2XP" \
  dotnet test src --filter "FullyQualifiedName~W3d|FullyQualifiedName~SageW3dCorpus"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Channel type 15 stays skipped. That is `WI-C8c-02`.
- If Generals output changes or a determinism test fails, stop and report per `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C8c-04` to `Ready` when its other prerequisites are done.
- Append a result line to `.exchange`.
