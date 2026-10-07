# Task Card `WI-G2-02`: Introduce the graphics settings type and the two presets

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G2-02` |
| Roadmap phase | [Track G · G2](../../track-g-rendering-clarity.md): Graphics settings & fidelity presets |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-G2-01` |
| Diagrams | [G2 architecture](../../deep-dives/g2-graphics-settings-presets.md#5-architecture-diagrams) |

## Goal

Add the engine-level graphics settings type carrying the knobs catalogued by `WI-G2-01`, with
`Faithful` and `Enhanced` presets, and expose it on the game so later Track G phases read it rather
than hardcoding state. This card adds the type and its defaults only; no render path consumes it
yet.

## Context to read (and only this)

- `docs/roadmap/implementation/track-g-g2-graphics-settings.md`: the `WI-G2-01` knob table. This is
  the authoritative list of members.
- The G2 deep dive and its architecture diagrams.
- `src/Panopticon.Simulation/Graphics/Rendering/Shadows/ShadowSettings.cs`: the closest existing pattern for
  a settings object with defaults and a validating setter.
- `src/Panopticon.Simulation/Lod/StaticGameLod.cs:60-135`: the property names to mirror where a knob already
  has an INI equivalent.

## Steps

1. Add the settings type with one member per knob from the audit table, using the property names the
   deep dive fixes. Default every member to the current hardcoded value recorded in the audit, so
   constructing it with defaults describes today's renderer exactly.
2. Add the two presets as static factory members. `Faithful` returns the current-behaviour defaults
   unchanged. `Enhanced` differs only on knobs the deep dive marks as safe to change in G2 itself;
   knobs owned by later phases stay at their `Faithful` value until that phase lands.
3. Expose the active settings on the game so render construction can reach it, following the
   `ShadowSettings` ownership pattern rather than adding a static or a singleton.
4. Add unit tests asserting that the `Faithful` preset's every member equals the audit's recorded
   current value, and that switching presets changes the active instance.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/` (new settings and preset types)
- `src/Panopticon.Simulation/Game.cs` (ownership of the active settings)
- `test/Panopticon.Simulation.Tests/` (preset tests)

## Acceptance criteria

- [ ] Every knob in the `WI-G2-01` table has a corresponding member.
- [ ] A test asserts the `Faithful` preset reproduces the pre-card hardcoded values member by member.
- [ ] No render path behaviour changes: the settings object is constructed but not yet consumed.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not wire the settings into `RenderPipeline`, the samplers, or the shaders. That is `WI-G2-03`.
- Do not add an experiment seam. G2 selects fixed pipeline state, per the G2 phase row.
- Do not change any current render behaviour. If wiring a member would require it, stop.
- If a knob from the audit cannot be represented without a public API break, mark this card
  `Blocked` and record which one.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Change `WI-G2-03` from `Blocked` to `Ready`.
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
