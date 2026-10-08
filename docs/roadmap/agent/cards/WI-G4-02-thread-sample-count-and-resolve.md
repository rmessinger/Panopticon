# Task Card `WI-G4-02`: Thread the sample count through the scene pass and add the MSAA resolve

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G4-02` |
| Roadmap phase | [Track G · G4](../../track-g-rendering-clarity.md): Multisample antialiasing |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `M` |
| Prerequisites | `WI-G4-01`, `WI-G3-04`, `WI-G2-03` |
| Diagrams | [G4 architecture](../../deep-dives/g4-msaa.md#5-architecture-diagrams) |

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
4. Resolve 3D colour into the single-sample composition target before `DrawingContext2D` draws
   UI, then blit that target. Keep 2D single-sample; classify auxiliary water/shadow passes
   separately and add only the resolves identified by the inventory.
5. Support off, 2x, 4x, and 8x, defaulting to off under `Faithful`. Select the highest supported
   count not exceeding the request for both colour and depth formats; report requested/effective counts.
6. Validate against the G3 harness at one sample and at four.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`
- `src/Panopticon.Simulation/Graphics/Shaders/` (the pipeline-owning shader resource classes)
- `src/Panopticon.Simulation/Graphics/` (the G2 settings consumption)

## Acceptance criteria

- [x] A frame renders and resolves correctly at 2x, 4x, and 8x across the 3D, terrain, water, road,
      and particle pipelines, with no validation-layer errors. 4x runs as the Enhanced preset in
      the full G3 harness; 2x and 8x were exercised by temporarily setting the preset count and
      running the same harness (all scenes captured, no `VeldridException`, no compatibility
      errors), then reverting to 4.
- [x] The G3 edge-aliasing metric improves measurably at 4x against the same frame at 1x:
      `unit-silhouettes` EdgeAliasing 0.0725 (1x) vs 0.0707 (4x), supersampled reference 0.0641
      vs 0.0627, `SilhouetteImproved` true for both presets.
- [x] A `Faithful` capture is unchanged against its golden: all six faithful scenes Passed with
      zero differing pixels on the pre-update gate run; faithful manifest hashes unchanged after
      the enhanced golden regeneration.
- [x] An unsupported sample count clamps rather than throwing: the G2 device clamp applies
      (request 64 clamps to the format limit, unit-tested), and counts above the session's
      construction count clamp back with a reported fallback string.
- [x] Standard validation passes: build 0 errors; tests 53 + 355 + 2089 passed, 7 skipped;
      `dotnet format --verify-no-changes` clean; brace check rc 0; `git diff --check` clean.

## Completion notes

- Enhanced goldens were regenerated on the reference host through the reviewed
  `TESTHARNESS_VR_UPDATE_GOLDENS=1` route; the committed manifest diff contains only the six
  enhanced hash/score triples plus `UpdatedUtc`.
- The composition framebuffer carries an inert depth attachment: 2D pipelines declare the depth
  format in their output description, and a colour-only framebuffer fails Veldrid's
  pipeline/framebuffer compatibility check (found on the Enhanced HUD scene).
- Sample-count changes take effect at construction (new `Game` constructor parameter); a
  mid-session swap reports a session fallback instead of rebuilding pipelines, per the recreation
  trigger table in the G2 plan.

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
- Change `WI-G5-02` from `Blocked` to `Ready` only after all its prerequisites are satisfied.
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
