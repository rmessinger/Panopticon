# Task Card `WI-G6-02`: Move shading to linear space with a dithered 8-bit output

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G6-02` |
| Roadmap phase | [Track G · G6](../../track-g-rendering-clarity.md): Gamma-correct rendering |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `L` |
| Prerequisites | `WI-G6-01`, `WI-G3-04`, `WI-G2-03` |
| Diagrams | [G6 architecture](../../deep-dives/g6-gamma-correct-rendering.md#5-architecture-diagrams) |

## Goal

Under `Enhanced`, shade in linear space and convert once on output, removing banding on sky, fog and
light falloff and stopping mip chains darkening with distance, while `Faithful` retains gamma-space
shading unchanged.

## Context to read (and only this)

- `docs/roadmap/implementation/track-g-g6-gamma-correct.md`: the `WI-G6-01` classification, which is
  the exact list of textures to convert and to leave alone.
- The G6 deep dive and its architecture diagrams.
- `src/Veldrid.ImageSharp/MipmapHelper.cs:36-61`.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs:92-100,160-168`.
- The G2 settings colour-space-mode member.

## Steps

1. Select sRGB texture views for the colour-map categories the audit identified, and only those.
   Data maps keep their existing linear formats.
2. Make the mip resample operate in linear space for colour maps: linearize, resample, re-encode.
   Leave data-map mip generation on the existing path.
3. Convert on output rather than per-material: encode once at the end of the frame, sourcing the
   colour-space mode from the G2 settings so `Faithful` skips the conversion entirely.
4. Add an ordered dither immediately before the 8-bit quantization, sized so it is invisible on flat
   areas but breaks up gradient banding.
5. Select a supported linear intermediate format with adequate precision as specified by the
   design. Record capabilities and fallback; keep auxiliary water passes and UI composition in
   the same linear contract. Decode UI colour art before composition without changing its layout/art.
6. Validate with the G3 harness on a fog and sky gradient scene and on a scene with strong mip
   minification, under both presets.

## Files likely to change

- `src/Panopticon.Simulation/Assets/Shaders/` (lighting and output encode)
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`
- `src/Veldrid.ImageSharp/MipmapHelper.cs`
- `src/Panopticon.Simulation/Graphics/` (texture view selection)

## Acceptance criteria

- [ ] A captured `Enhanced` frame shows no visible banding on a fog or sky gradient, satisfying the
      G6 milestone.
- [ ] Mip chains no longer darken with distance under `Enhanced`.
- [ ] A `Faithful` capture is unchanged against its golden.
- [ ] No data map is treated as sRGB.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add HDR, exposure control, or a filmic tonemap beyond what the deep dive specifies.
- Change UI colour transfer only as required for correct linear composition; G9 owns UI quality/art.
- Do not change anisotropy, LOD bias, or shadow settings while here.
- If the backend cannot support the documented intermediate/output contract, record the
  limitation and block rather than silently applying double encoding or premature quantization.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the G6 phase Status in [`track-g-rendering-clarity.md`](../../track-g-rendering-clarity.md).
- Tick the G6 clause in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
