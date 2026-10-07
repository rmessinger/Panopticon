# Task Card `WI-G2-03`: Wire the graphics settings into render resource construction

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G2-03` |
| Roadmap phase | [Track G · G2](../../track-g-rendering-clarity.md): Graphics settings & fidelity presets |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-G2-02` |
| Diagrams | [G2 architecture](../../deep-dives/g2-graphics-settings-presets.md#5-architecture-diagrams) |

## Goal

Make the render resources that G4 through G8 will vary read their values from the G2 settings object
instead of hardcoded literals, so later phases change a setting rather than an implementation. The
observable output must not change while both presets still carry the current values.

## Context to read (and only this)

- `docs/roadmap/implementation/track-g-g2-graphics-settings.md`: the consumer column of the
  `WI-G2-01` table lists exactly the call sites to change.
- The G2 deep dive and its architecture diagrams.
- `src/Panopticon.Simulation/Graphics/StandardGraphicsResources.cs:22-48`: the three sampler constructions.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs:92-100`: intermediate target creation.
- `src/Panopticon.Simulation/Graphics/Rendering/Shadows/ShadowSettings.cs:9-77`.

## Steps

1. Pass the settings object into the constructors listed in the audit's consumer column. Prefer
   constructor injection over reaching through the game object, matching how
   `StandardGraphicsResources` already receives its `GraphicsDevice`.
2. Replace each hardcoded literal identified by the audit with the matching settings member. Change
   the source of the value only; do not change the value.
3. Where a setting cannot take effect without recreating a resource, record the recreation trigger
   in the implementation plan rather than implementing hot-reload here.
4. Add a test that constructs the render resources under both presets and asserts the resulting
   descriptions differ only on the members the `Enhanced` preset actually changes.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/StandardGraphicsResources.cs`
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`
- `src/Panopticon.Simulation/Graphics/Rendering/Shadows/ShadowSettings.cs`
- `test/Panopticon.Simulation.Tests/`
- `docs/roadmap/implementation/track-g-g2-graphics-settings.md`

## Acceptance criteria

- [ ] No literal remains at any file:line the audit listed as a settings-owned knob.
- [ ] A captured frame under `Faithful` is byte-identical to the pre-card capture of the same scene.
- [ ] Knobs requiring resource recreation are documented, not silently ignored.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement MSAA, alpha-to-coverage, sRGB, anisotropy changes, or shadow tiers. Those are
  G4 through G8. This card only moves the source of the value.
- Do not add a settings UI.
- If threading a setting into a call site requires restructuring the render pass order, stop and
  mark `Blocked`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the G2 phase Status in [`track-g-rendering-clarity.md`](../../track-g-rendering-clarity.md).
- Tick the G2 clause in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
