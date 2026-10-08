# Task Card `WI-G7-02`: Configurable anisotropy, mip LOD bias, and terrain mip quality

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G7-02` |
| Roadmap phase | [Track G · G7](../../track-g-rendering-clarity.md): Texture filtering & mip quality |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `M` |
| Prerequisites | `WI-G7-01`, `WI-G3-04`, `WI-G2-03` |
| Diagrams | [G7 architecture](../../deep-dives/g7-texture-filtering.md#5-architecture-diagrams) |

## Goal

Make anisotropy configurable up to 16x with a mip LOD bias, and raise the quality of the terrain
texture-array mip path, so terrain and ground-plane textures stay legible at grazing angles without
shimmer under camera motion.

## Context to read (and only this)

- `docs/roadmap/implementation/track-g-g7-texture-filtering.md`: the `WI-G7-01` audit, which is the
  exact list of samplers, consumers, and resource sets to change.
- The G7 deep dive and its architecture diagrams.
- `src/Panopticon.Simulation/Graphics/StandardGraphicsResources.cs:22-48`.
- `src/Panopticon.Simulation/Terrain/Terrain.cs:441-533`.
- The G2 settings anisotropy and mip-bias members.

## Steps

1. Replace the fixed anisotropic sampler with one built from the G2 anisotropy setting, clamped to
   the device's reported maximum. Keep the existing address modes exactly.
2. Add the mip LOD bias from the G2 settings to the sampler descriptions the audit identified as
   needing it. If the audit found the bias is not expressible in the sampler description, apply it
   in the shader sample call instead, and only on the paths the audit listed.
3. Rebuild the resource sets the audit named when the samplers are recreated, so a settings change
   takes effect without a restart.
4. Keep the existing Lanczos3 terrain resample and generated mip chain unless the audit proves a
   defect. Copying pre-generated levels through staging is not missing mip generation. Test array
   dimensions, seams and mip provenance; keep G5 alpha/G6 colour policy compatible.
5. Validate with the G3 harness on a grazing-angle terrain scene, comparing the sharpness metric at
   4x and 16x, and checking a short camera-motion capture for shimmer.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/StandardGraphicsResources.cs`
- `src/Panopticon.Simulation/Terrain/Terrain.cs`
- `src/Panopticon.Simulation/Graphics/` (resource set rebuild)

## Acceptance criteria

- [ ] Terrain and ground-plane textures stay legible at grazing angles at 16x with no shimmer under
      camera motion, measured by the G3 metrics, satisfying the G7 milestone.
- [ ] Anisotropy above the device maximum clamps rather than throwing.
- [ ] A `Legacy` capture is unchanged against its golden.
- [ ] Changing the anisotropy setting takes effect without restarting the game.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change the sampler address modes; changing them alters texture wrapping across the renderer.
- Do not change colour space or alpha test while here.
- Do not add a new resampler dependency; use one already present in the file.
- If the mip bias is expressible in neither the sampler nor the affected shaders, stop and mark
  `Blocked`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the G7 phase Status in [`track-g-rendering-clarity.md`](../../track-g-rendering-clarity.md).
- Tick the G7 clause in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
