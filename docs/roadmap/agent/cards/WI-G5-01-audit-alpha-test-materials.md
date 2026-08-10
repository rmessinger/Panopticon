# Task Card `WI-G5-01`: Audit alpha-tested materials and the cutout mip path

| Field | Value |
|---|---|
| Card ID | `WI-G5-01` |
| Roadmap phase | [Track G · G5](../../track-g-rendering-clarity.md): Cutout & alpha-test clarity |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | None: G5 has no deep dive yet. This card is one of the investigations that produces the material for one. |

## Goal

Record which geometry actually goes through the fixed alpha-test cutoff, and how the mip chains for
those textures are produced, so the screen-door cause can be attributed before any fix is designed.
Read-only: no code changes.

## Context to read (and only this)

- `src/OpenSage.Game/Assets/Shaders/Common.h:29-33`: `FailsAlphaTest` and the `0.37647f` cutoff.
- Every shader in `src/OpenSage.Game/Assets/Shaders/` that calls `FailsAlphaTest`. Find them with a
  repository search.
- `src/Veldrid.ImageSharp/MipmapHelper.cs:20-61`: `ComputeMipLevels` and `GenerateMipmaps`, including
  the resampler it uses.
- `src/OpenSage.Game/Terrain/Terrain.cs:441-533`: the terrain texture-array mip copy path, which is
  a second, separate mip route.

## Steps

1. List every shader calling `FailsAlphaTest`, and for each, the material or draw path that uses it.
   Name the actual asset categories affected rather than assuming foliage.
2. Record how mip chains are generated for the textures those materials sample: the resampler used,
   whether alpha is filtered with colour, and whether any coverage preservation exists. State the
   resampler by name; do not assume it is a box filter.
3. Record whether alpha-tested textures are distinguishable from non-alpha-tested ones at load time.
   Coverage-preserving mip generation must apply only to the former, so if no flag exists, say so.
4. Record whether the cutoff is reachable from anywhere other than the shader constant, since a
   per-mip threshold rescale needs a place to put the rescaled value.
5. Record whether the terrain texture-array path and the general texture path would both need the
   fix, or only one.
6. Write the findings into a new `docs/roadmap/implementation/track-g-g5-alpha-test-clarity.md`,
   following the format of [`track-g-g1-high-dpi.md`](../../implementation/track-g-g1-high-dpi.md).

## Files likely to change

- `docs/roadmap/implementation/track-g-g5-alpha-test-clarity.md` (new)

## Acceptance criteria

- [ ] Every `FailsAlphaTest` caller is listed with its material path.
- [ ] The mip resampler is named exactly as found in the code.
- [ ] Whether alpha-tested textures are identifiable at load time is answered definitively.
- [ ] Both mip routes are assessed for whether they need the fix.
- [ ] No `.cs` or shader file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not change the cutoff, the shaders, or the mip generation. That is `WI-G5-02`.
- Do not correct roadmap prose about the cause; record what the code does and let the deep dive
  reconcile it.
- Do not assess Track F terrain work.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - None; `WI-G5-02` already exists and depends on these findings.
