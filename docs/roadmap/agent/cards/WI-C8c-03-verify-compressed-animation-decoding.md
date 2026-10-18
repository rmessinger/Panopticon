# Task Card `WI-C8c-03`: Verify adaptive-delta and motion-channel decoding across the corpus

| Field | Value |
|---|---|
| Card ID | `WI-C8c-03` |
| Roadmap phase | [Track C · C8c](../../track-c-external-assets.md): BFME-family skinning and animation |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C8a-04` |
| Diagrams | [C8 architecture diagrams](../../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams) |

## Goal

Prove with installed tests that every converted BFME-family animation produces well-formed channels, and record whether pivot 0 motion channels carry root motion.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` sections 3, 4.2, and 8.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dAnimationImporter.cs`: `ConvertCompressed`.
- `src/Panopticon.FileFormats.W3d/W3dAdaptiveDeltaCodec.cs`, `W3dMotionChannel*.cs`.
- `test/Panopticon.Simulation.Tests/Data/W3d/SageW3dCorpusTests.cs`.

## Steps

1. Add installed tests (gated per game) over every animation that converts: each channel has at least one keyframe; keyframe times are non-decreasing and within `[0, duration]`; quaternion keyframes have length within `1e-3` of 1; translation values are finite.
2. Cover the five BFME2 adaptive-delta (flavor 1) files by name and every motion-channel delta type (`TimeCoded`, `Delta4`, `Delta8`).
3. For BFME2 clips with pivot 0 motion channels (446 expected), report the summed translation per clip. Record in deep dive section 8 whether pivot 0 moves (root motion) and in which clips.
4. If a check fails, stop and report the clip, channel, and values; do not change decoders in this card.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Content/Importing/SageW3dAnimationCorpusTests.cs` (new)
- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` (root-motion finding)

## Acceptance criteria

- [ ] Step 1 and 2 checks pass for BFME, BFME2, and RotWK.
- [ ] Section 8 records the root-motion finding.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
BFME_PATH="/path/to/BFME" BFME2_PATH="/path/to/BFME2" BFME2_ROTWK_PATH="/path/to/BFME2XP" \
  dotnet test src --filter "FullyQualifiedName~SageW3dAnimationCorpus"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No decoder changes; failures are escalated.
- Do not commit game files or extracted bytes.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C8c-04` to `Ready` when its other prerequisites are done.
- Append a result line to `.exchange`.
