# Task Card `WI-G5-02`: Alpha-to-coverage and coverage-preserving cutout mips

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G5-02` |
| Roadmap phase | [Track G · G5](../../track-g-rendering-clarity.md): Cutout & alpha-test clarity |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `M` |
| Prerequisites | `WI-G5-01`, `WI-G4-02` |
| Diagrams | [G5 architecture](../../deep-dives/g5-alpha-test-clarity.md#5-architecture-diagrams) |

## Goal

Stop minified alpha-tested geometry dissolving into a dotted lattice, by enabling alpha-to-coverage
when MSAA is active and generating mip chains that preserve cutout coverage, while `Legacy` keeps
the original hard cutoff.

## Context to read (and only this)

- `docs/roadmap/implementation/track-g-g5-alpha-test-clarity.md`: the `WI-G5-01` findings, which fix
  the exact materials, mip routes, and cutoff sites to change.
- The G5 deep dive and its architecture diagrams.
- `src/Panopticon.Simulation/Assets/Shaders/Common.h:29-33`.
- `src/Veldrid.ImageSharp/MipmapHelper.cs:36-61`.
- The G2 settings alpha-test-mode member and the G4 sample count.

## Steps

1. Add the alpha-test mode to the shader path so the cutoff behaviour is selectable: hard cutoff for
   `Legacy`, alpha-to-coverage for `Enhanced` when the sample count is above one. Fall back to the
   hard cutoff when MSAA is off, since alpha-to-coverage has no effect at one sample.
2. Enable alpha-to-coverage in the blend state of the pipelines owning the materials the audit listed,
   and only those.
3. Add coverage-preserving mip generation for textures the audit identified as alpha-tested: after
   each downsample, rescale the alpha so the fraction of texels passing the threshold matches the
   source level. Leave non-alpha-tested textures on the existing path.
4. Bake coverage correction into mip alpha once, keeping the hard cutoff unchanged; do not also
   rescale a runtime threshold. Separate cutout/blended/opaque roles in texture cache keys.
   Rebuild authored DDS cutout mips only through a tested decode path and regenerate JPG mips
   after applying any alpha sidecar.
5. Validate with the G3 harness on a scene containing minified foliage and fencing, at 1x and 4x, and
   under both presets.

## Files likely to change

- `src/Panopticon.Simulation/Assets/Shaders/Common.h`
- `src/Panopticon.Simulation/Assets/Shaders/` (the calling shaders)
- `src/Panopticon.Simulation/Graphics/Shaders/` (blend state for the affected pipelines)
- `src/Veldrid.ImageSharp/MipmapHelper.cs`

## Acceptance criteria

- [ ] Minified alpha-tested foliage and fencing show no dissolve-to-dots at distance under
      `Enhanced`, with cutout silhouettes preserved, satisfying the G5 milestone.
- [ ] A `Legacy` capture is unchanged against its golden, including the hard cutoff result.
- [ ] Alpha-to-coverage is enabled only on the pipelines the audit listed.
- [ ] Non-alpha-tested textures use the unchanged mip path.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change colour space or lighting. That is G6.
- Do not change anisotropy or LOD bias. That is G7.
- Do not apply coverage preservation to every texture; it is wrong for non-cutout alpha.
- If enabling alpha-to-coverage requires reordering the transparent pass, stop and mark `Blocked`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the G5 phase Status in [`track-g-rendering-clarity.md`](../../track-g-rendering-clarity.md).
- Tick the G5 clause in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
