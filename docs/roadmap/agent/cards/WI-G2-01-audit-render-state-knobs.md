# Task Card `WI-G2-01`: Audit fixed render-state knobs and the unused LOD asset data

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G2-01` |
| Roadmap phase | [Track G · G2](../../track-g-rendering-clarity.md): Graphics settings & fidelity presets |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [G2 architecture](../../deep-dives/g2-graphics-settings-presets.md#5-architecture-diagrams) |

## Goal

Produce the authoritative list of every render-state value that the G2 settings layer must own
(sample count, alpha-test mode, colour-space mode, anisotropy, mip bias, shadow tier), recording
for each one where it is currently hardcoded and who consumes it. Read-only: no code changes.

## Context to read (and only this)

- `src/Panopticon.Simulation/Lod/StaticGameLod.cs:60-135`: the parsed per-level properties, including
  `SampleCount2D`, `SampleCount3D`, `TextureReductionFactor`, `UseAnisotropic`, `UseShadowMapping`,
  `ShadowLOD`.
- `src/Panopticon.Simulation/Lod/DynamicGameLod.cs:30-37` and `src/Panopticon.Simulation/Lod/LodPreset.cs:29-42`.
- `src/Panopticon.Simulation/Content/AssetStore.cs`: the `StaticGameLods` and `DynamicGameLods` collection
  registrations.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs:23-25,92-100`: `GameOutputDescription`
  and the intermediate target creation.
- `src/Panopticon.Simulation/Graphics/StandardGraphicsResources.cs:13-48`: the three fixed samplers.
- `src/Panopticon.Simulation/Graphics/Rendering/Shadows/ShadowSettings.cs:9-77`: the shadow defaults.
- `src/Panopticon.Simulation/Assets/Shaders/Common.h:29-33`: the fixed alpha-test threshold.

## Steps

1. Search `src/` for every read of the `StaticGameLods`, `DynamicGameLods`, and `LodPresets` asset
   collections. Record each consumer, or record explicitly that a collection has no consumer.
2. Build a table with one row per settings knob named in the G2 phase row. For each record: the
   knob, the current hardcoded value, the file:line that fixes it, and every consumer that would
   have to read it from a settings object instead.
3. For the sample count, record every `TextureDescription` and `GraphicsPipelineDescription` that
   would need the value threaded through it. Do not design the threading, just count and list.
4. Record which knobs are already expressible in the parsed LOD assets and which have no INI
   representation at all, since that decides whether the settings layer can be seeded from game
   data or must be engine-only.
5. Fill the audit section in `docs/roadmap/implementation/track-g-g2-graphics-settings.md`,
   following the format of [`track-g-g1-high-dpi.md`](../../implementation/track-g-g1-high-dpi.md):
   a short human summary above the `---` divider, the tables below it.

## Files likely to change

- `docs/roadmap/implementation/track-g-g2-graphics-settings.md`

## Acceptance criteria

- [x] Every knob named in the G2 phase row has a row with its current hardcoded value and file:line.
- [x] The number of runtime consumers of `StaticGameLods` is stated explicitly, including if it is
      zero.
- [x] The list of pipeline and texture descriptions needing a sample count is complete and counted.
- [x] Knobs with no INI representation are distinguished from those with one.
- [x] No `.cs` file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not design the settings type or the preset shape. That needs the G2 deep dive.
- Do not change any render state, even where a value is obviously wrong.
- Do not extend the audit to Track B's instancing work; record only what G2 must own.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`, including the count of knobs with no INI
  representation.
- Follow-up cards to create:
  - None; `WI-G2-02` and `WI-G2-03` already exist and depend on these tables.
