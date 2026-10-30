# Task Card `WI-C12b-01`: RMV2 and `VMPF` mesh layout investigation

| Field | Value |
|---|---|
| Card ID | `WI-C12b-01` |
| Roadmap phase | [Track C · C12b](../../track-c-external-assets.md): SHOGUN 2 static models and parts |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `M` |
| Prerequisites | `WI-C12a-01` |
| Diagrams | [C12 architecture diagrams](../../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams) |

## Goal

The mesh headers, vertex formats, strides, index layouts, and texture-directory records of `RMV2` versions 0 to 2 and `VMPF` version 3 are recorded in the deep dive, with counts of each format across the install.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 3, 4, 10.
- `src/Panopticon.IO/PackFileSystem.cs` from `WI-C12a-01`.
- RPFM and AssetEditor RMV2 notes, factual reference only (section 8).

## Steps

1. Write a throwaway probe outside the repository (for example under `/tmp`) or an Interactive test that dumps headers through `PackFileSystem`.
2. For each RMV2 version, record the LOD record layout, mesh header fields, vertex format ids with stride and attribute order (position, normal, tangent, UV sets, colour, bone indices and weights), index width, and how texture paths are stored.
3. For `VMPF` version 3, record the same plus the half-float position encoding, skin influence count and encoding, and the bone name or index scheme.
4. Check the 6 RMV2 files over 3 MB and the largest vertex count against the 65,535 vertex limit.
5. Confirm the `texture_mra` channel order, the normal-map swizzle (BC3 with X in alpha or plain), and what `texture_colour_mask` channels hold, by sampling decoded pixels from the proof parts.
6. Write the results into deep dive sections 3 and 4 as tables, replacing the "not decoded yet" and "Open:" sentences, and refine `WI-C12b-03` and `WI-C12b-04` steps if a format needs extra handling.

## Files likely to change

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 3, 4, 9
- `docs/roadmap/agent/cards/WI-C12b-03-rmv2-static-import.md`, `WI-C12b-04-part-import-and-materials.md` (refinements only)

## Acceptance criteria

- [ ] Every RMV2 and `VMPF` vertex format seen in the install is listed with a file count, or the remainder is named as unknown with an example path.
- [ ] MRA order, normal swizzle, and colour-mask channels are stated with the sampled evidence.
- [ ] The vertex-limit question is answered with the largest observed mesh.
- [ ] No source code is committed by this card.

## Validation commands

```sh
git diff --name-only origin/main -- src test   # expect no output
```

## Out of scope / guardrails

- Import code (`WI-C12b-03`).
- Animation frame data (`WI-C12c-01`).
- No SHOGUN 2 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C12b-03` to `Ready`.
- Append a result line to `.exchange`.
