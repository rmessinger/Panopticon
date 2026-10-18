# Task Card `WI-C8c-02`: Investigate animation channel type 15

| Field | Value |
|---|---|
| Card ID | `WI-C8c-02` |
| Roadmap phase | [Track C · C8c](../../track-c-external-assets.md): BFME-family skinning and animation |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-C8a-04` |
| Diagrams | [C8 architecture diagrams](../../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams) |

## Goal

Identify what channel type 15 (`W3dAnimationChannelType.UnknownBfme`, commented "probably Opacity") carries and record whether C8 decodes it or keeps skipping it.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` sections 4.1 and 6.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dAnimationImporter.cs`: the three `UnknownBfme` skips.
- `src/Panopticon.FileFormats.W3d/W3dAnimationChannelType.cs`, `W3dAnimationChannel.cs`, `W3dTimeCodedAnimationChannel.cs`, `W3dMotionChannel.cs`.

## Steps

1. Over every BFME, BFME2, and RotWK animation, collect for type 15 channels: vector length, value range, pivot names (from the matching skeleton), and whether the same pivot also has a visibility bit channel.
2. Expected counts from section 4.1: 1878 BFME, 44,161 BFME2 (14,839 in motion channels), 1424 RotWK. Report any difference.
3. Decide from the evidence: opacity (values in 0 to 1, on mesh-bearing pivots, fades in death or spawn clips), or something else. Check whether `ImportedAnimationChannelType` and the runtime can express it.
4. Record the finding and decision in deep dive section 6. If decoding is chosen, add follow-up card `WI-C8c-05` (channel type, runtime application, tests); if not, state that the skip stays and C8c-04 proofs accept it.

## Files likely to change

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md`
- `docs/roadmap/agent/cards/WI-C8c-05-*.md` (new, only if decoding)

## Acceptance criteria

- [ ] Section 6 records the meaning of type 15 with counts, value ranges, and example clips.
- [ ] A follow-up card exists if and only if the decision is to decode.

## Validation commands

```sh
# Documentation only. Scratch tools stay outside the repository.
```

## Out of scope / guardrails

- No source edits.
- Do not commit game files or extracted bytes.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C8c-04` to `Ready` when its other prerequisites are done.
- Append a result line to `.exchange`.
