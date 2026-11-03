# Task Card `WI-G13-05`: Sky and distance fog

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G13-05` |
| Roadmap phase | [Track G · G13](../../track-g-rendering-clarity.md): First-person ground view |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-G13-04` |
| Diagrams | [G13 architecture](../../deep-dives/g13-first-person-ground-view.md#5-architecture-diagrams) |

## Goal

Replace the grey clear colour with a procedural sky and hide the far-plane cut with distance fog, both active only in first person.

## Context to read (and only this)

- [G13 design](../../deep-dives/g13-first-person-ground-view.md) section 3.4.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`, `Terrain/Weather.cs`, terrain lighting and `GlobalShaderResources`.
- `src/Panopticon.Simulation/Assets/Shaders/Terrain.frag`, `Road.frag`, the W3D and PBR object shaders, water shaders.

## Steps

1. Add `SkyRenderer`: a gradient dome (zenith, horizon, ground colours from the map's time-of-day lighting) drawn before terrain at depth far.
2. Add a shared fog include and `FogEnabled`, `FogStart`, `FogEnd`, `FogColour` global constants; apply in terrain, road, object, particle, and water fragment shaders. Fog colour matches the sky horizon.
3. Use `Weather.HardwareFogStart/End` when the map sets them, else defaults scaled to `FirstPersonFarPlane`.
4. `FogEnabled` and the sky are off in `Tactical`; assert shader output is unchanged there by goldens.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/Rendering/SkyRenderer.cs` (new), `RenderPipeline.cs`, global shader resources
- `src/Panopticon.Simulation/Assets/Shaders/Fog.h`, `Sky.vert`, `Sky.frag` (new), the fragment shaders above

## Acceptance criteria

- [ ] The horizon shows sky with no grey band and no visible far-plane cut.
- [ ] Existing G3 goldens unchanged.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario VisualRegression --headless
```

## Out of scope / guardrails

- Clouds, weather particles at ground level, and Earth curvature ([Track R R6](../../track-r-real-world-terrain.md)).
- Presentation only: no `GameObject`, order, or logic random. A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-G13-06` to `Ready`.
- Tick G13-4 in the checklist and update the README progress row.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
