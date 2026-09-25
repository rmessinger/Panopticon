# Task Card `WI-G4-02`: Thread the sample count through the scene pass and add the MSAA resolve

| Field | Value |
|---|---|
| Card ID | `WI-G4-02` |
| Roadmap phase | [Track G · G4](../../track-g-rendering-clarity.md): Multisample antialiasing |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-G4-01`, `WI-G3-03`, `WI-G2-03`, plus a G4 deep dive with populated architecture diagrams (the [agent guide](../README.md#how-to-maintain-this-guide) gate) |
| Diagrams | Blocked on the G4 deep dive. Do not mark this card `Ready` until its Architecture Diagrams section exists and is not a placeholder. |

## Goal

Make the scene pass render at the sample count carried by the G2 settings, resolving before the
final blit, so 4x MSAA renders correctly across the 3D, terrain, water, road, and particle pipelines
while the `Faithful` preset stays at one sample and unchanged.

## Context to read (and only this)

- `docs/roadmap/implementation/track-g-g4-msaa.md`: the `WI-G4-01` inventory, which is the exact
  change list.
- The G4 deep dive and its architecture diagrams.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs:23-25,92-100,160-168`.
- The G2 settings type and its sample-count member.

## Steps

1. Add the sample count to the intermediate colour and depth target descriptions, sourced from the
   G2 settings rather than a literal.
2. Give the shared output description a per-pass sample count so passes the inventory marked
   single-sampled are unaffected. If the inventory found the description is shared across differing
   needs, split it as the deep dive specifies rather than raising the sample count everywhere.
3. Pass the sample count into every game-owned `GraphicsPipelineDescription` listed by the inventory,
   through the shader resource classes that construct them.
4. Add the resolve before the final `TextureCopier` blit, and at every other resolve site the
   inventory identified. Resolve into a single-sampled texture and blit from that.
5. Support off, 2x, 4x, and 8x, defaulting to off under `Faithful` and clamping to the device's
   reported maximum rather than failing when a sample count is unsupported.
6. Validate against the G3 harness at one sample and at four.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`
- `src/Panopticon.Simulation/Graphics/Shaders/` (the pipeline-owning shader resource classes)
- `src/Panopticon.Simulation/Graphics/` (the G2 settings consumption)

## Acceptance criteria

- [ ] A frame renders and resolves correctly at 2x, 4x, and 8x across the 3D, terrain, water, road,
      and particle pipelines, with no validation-layer errors.
- [ ] The G3 edge-aliasing metric improves measurably at 4x against the same frame at 1x, satisfying
      the G4 milestone.
- [ ] A `Faithful` capture is unchanged against its golden.
- [ ] An unsupported sample count clamps rather than throwing.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add alpha-to-coverage. That is `WI-G5-02` and depends on this card.
- Do not multisample the shadow depth pass or any pass the inventory marked single-sampled.
- Do not change shading, colour space, or filtering while here.
- If a pipeline cannot accept a sample count without restructuring its render pass, stop and mark
  `Blocked` naming the pipeline.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the G4 phase Status in [`track-g-rendering-clarity.md`](../../track-g-rendering-clarity.md).
- Tick the G4 clause in [`implementation-checklist.md`](../../implementation-checklist.md).
- Change `WI-G5-02` from `Blocked` to `Ready` once G5's diagram gate is also satisfied.
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
