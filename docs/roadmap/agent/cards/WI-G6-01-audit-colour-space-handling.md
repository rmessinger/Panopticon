# Task Card `WI-G6-01`: Audit colour-space handling across textures, shading, mips and output

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G6-01` |
| Roadmap phase | [Track G · G6](../../track-g-rendering-clarity.md): Gamma-correct rendering |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [G6 architecture](../../deep-dives/g6-gamma-correct-rendering.md#5-architecture-diagrams) |

## Goal

Record where the renderer currently treats data as sRGB and where as linear, at every stage from
texture upload through shading to the presented frame, so the linear-space conversion can be scoped
without breaking the assets that are already linear. Read-only: no code changes.

## Context to read (and only this)

- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs:23-25,92-100,160-168`: the output formats
  of the intermediate target and the swapchain target.
- `src/Veldrid.ImageSharp/MipmapHelper.cs:36-61`: the mip resample, and which colour space it
  operates in.
- `src/Panopticon.Simulation/Graphics/StandardGraphicsResources.cs`: the texture formats used for the
  placeholder and solid textures.
- `src/Panopticon.Simulation/Assets/Shaders/`: the lighting computation in the fixed-function and
  normal-mapped fragment shaders.
- `src/Panopticon.Simulation/Terrain/Terrain.cs:480-533`: the terrain texture-array format and resample.

## Steps

1. Classify every texture category by what its data means: colour maps that should be sRGB, and
   data maps such as normal, alpha, specular, and mask textures that must stay linear. Record the
   load site of each category.
2. Record every `PixelFormat` used for a sampled texture and for a render target, and whether an
   sRGB variant is selected anywhere today.
3. Record where lighting arithmetic happens and confirm whether its inputs are gamma-space texture
   values, which is the specific defect G6 targets.
4. Record the colour space of the mip resample, naming the resampler exactly as found rather than
   assuming a box filter.
5. Record the final output path: the intermediate format, the blit, and the swapchain format, and
   whether any conversion or tonemap exists.
6. Record whether any dither exists before the 8-bit output.
7. Fill the audit section in `docs/roadmap/implementation/track-g-g6-gamma-correct.md`, following
   the format of [`track-g-g1-high-dpi.md`](../../implementation/track-g-g1-high-dpi.md).

## Files likely to change

- `docs/roadmap/implementation/track-g-g6-gamma-correct.md`

## Acceptance criteria

- [ ] Every texture category is classified as colour or data with its load site.
- [ ] The mip resampler is named exactly as found in the code.
- [ ] Whether lighting currently operates on gamma-space inputs is answered definitively.
- [ ] The full output chain's formats are recorded, and the presence or absence of a dither stated.
- [ ] No `.cs` or shader file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not change any format, shader, or resample. That is `WI-G6-02`.
- Do not evaluate HDR or a wide-gamut output; G6 targets linear shading with an 8-bit output.
- Do not assess the 2D and UI path's colour space here; G9 owns that surface.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`, including the count of data-map categories that
  must stay linear.
- Follow-up cards to create:
  - None; `WI-G6-02` already exists and depends on this classification.
