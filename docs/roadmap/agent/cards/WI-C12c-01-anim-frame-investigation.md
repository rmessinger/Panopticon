# Task Card `WI-C12c-01`: Version 1 `.anim` frame data investigation

| Field | Value |
|---|---|
| Card ID | `WI-C12c-01` |
| Roadmap phase | [Track C · C12c](../../track-c-external-assets.md): SHOGUN 2 animation and composite soldiers |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `M` |
| Prerequisites | `WI-C12a-01` |
| Diagrams | [C12 architecture diagrams](../../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams) |

## Goal

The frame data that follows the bone table in version 1 `.anim` clips is decoded and recorded in the deep dive, including key encoding, per-bone channel masks, and how the 53 frame-rate-first files differ.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 5, 10.
- `src/Panopticon.IO/PackFileSystem.cs` from `WI-C12a-01`.
- RPFM and AssetEditor animation notes, factual reference only.

## Steps

1. Dump clips through a throwaway probe outside the repository or an Interactive test.
2. Record the layout after the bone table: frame count, per-bone translation and rotation presence, quantization (for example normalized i16 quaternions), and any static-bone or constant-track sections.
3. Verify the decode by checking that `yari_combat_idle1` frame 0 matches the bind pose implied by the proof head part and that rotations are unit length.
4. Classify the 53 files that start with a frame rate and decide whether C12c supports them; record the decision.
5. Write the results into deep dive section 5, replacing "not decoded yet", and refine `WI-C12c-02` steps.

## Files likely to change

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` section 5
- `docs/roadmap/agent/cards/WI-C12c-02-anim-import.md` (refinements only)

## Acceptance criteria

- [ ] The frame layout is recorded with field widths and an example clip.
- [ ] At least the four proof clips decode with unit-length rotations.
- [ ] The frame-rate-first files have a recorded support decision.
- [ ] No source code is committed by this card.

## Validation commands

```sh
git diff --name-only origin/main -- src test   # expect no output
```

## Out of scope / guardrails

- Import code (`WI-C12c-02`).
- No SHOGUN 2 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C12c-02` to `Ready`.
- Append a result line to `.exchange`.
