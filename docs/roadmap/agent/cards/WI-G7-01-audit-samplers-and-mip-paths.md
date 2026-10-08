# Task Card `WI-G7-01`: Audit sampler creation, consumers, and the terrain mip path

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G7-01` |
| Roadmap phase | [Track G · G7](../../track-g-rendering-clarity.md): Texture filtering & mip quality |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [G7 architecture](../../deep-dives/g7-texture-filtering.md#5-architecture-diagrams) |

## Goal

Record how samplers are constructed and shared today, and which draw paths would change if
anisotropy became configurable and a mip LOD bias were added, so G7's blast radius is known before
any sampler is touched. Read-only: no code changes.

## Context to read (and only this)

- `src/Panopticon.Simulation/Graphics/StandardGraphicsResources.cs:13-48`: the three fixed samplers,
  including `Aniso4xClampSampler` and the `SamplerDescription.Aniso4x` preset it derives from.
- Every consumer of those samplers. Find them with a repository search for each property name.
- `src/Panopticon.Simulation/Terrain/Terrain.cs:441-533`: the terrain texture-array creation, the resample,
  and the per-mip copy loop.
- `src/Veldrid.ImageSharp/MipmapHelper.cs:20-61`.

## Steps

1. Record each sampler property, its exact name as it appears in the code, its full description
   including address modes, and whether the instances are shared or created per consumer.
2. List every consumer of each sampler with file:line and the draw path it serves. Note which
   consumers are terrain or ground-plane paths, since those are what the G7 milestone measures.
3. Record whether any sampler description exposes a mip LOD bias today, and whether the wrapping
   graphics abstraction allows setting one.
4. Record the terrain texture-array mip route separately from the general mip route, including which
   levels are generated versus copied, and the resampler used at each step.
5. Record what would have to change for anisotropy to be configurable: whether samplers can be
   recreated at runtime, and which resource sets would need rebuilding.
6. Fill the audit section in `docs/roadmap/implementation/track-g-g7-texture-filtering.md`,
   following the format of [`track-g-g1-high-dpi.md`](../../implementation/track-g-g1-high-dpi.md).

## Files likely to change

- `docs/roadmap/implementation/track-g-g7-texture-filtering.md`

## Acceptance criteria

- [ ] Each sampler is recorded with its exact code name and full description.
- [ ] Every sampler consumer is listed with file:line and draw path.
- [ ] Whether a mip LOD bias is expressible today is answered definitively.
- [ ] The resource sets requiring rebuild on sampler recreation are named.
- [ ] No `.cs` file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not change any sampler or the terrain path. That is `WI-G7-02`.
- Do not assess colour space; G6 owns that.
- Do not benchmark. This card is a structural audit.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - None; `WI-G7-02` already exists and depends on these findings.
