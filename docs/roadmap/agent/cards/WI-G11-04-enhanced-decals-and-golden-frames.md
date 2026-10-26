# Task Card `WI-G11-04`: Enhanced decal quality and golden frames

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G11-04` |
| Roadmap phase | [Track G · G11](../../track-g-rendering-clarity.md): Terrain impact decals |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `S` |
| Prerequisites | `WI-G11-02`, `WI-G11-03` |
| Diagrams | [G11 architecture](../../deep-dives/g11-terrain-impact-decals.md#5-architecture-diagrams) |

## Goal

Light decals with the terrain's lighting under `Enhanced`, set its decal limit, and guard both presets with G3 golden frames.

## Context to read (and only this)

- [G11 design](../../deep-dives/g11-terrain-impact-decals.md) sections 3 and 4.
- `src/Panopticon.Simulation/Graphics/GraphicsSettings.cs`, `GraphicsSettingsController.cs`.
- `src/Panopticon.Simulation/Assets/Shaders/Terrain.frag` lighting, cloud, and shadow terms.
- `src/Panopticon.Tools.TestHarness/VisualRegressionRunner.cs`, `src/Panopticon.Tools.ReplaySketch.Core/Profiles/visual-regression-scenes.json`.

## Steps

1. Add `GraphicsSettings.TerrainDecalLighting` (`LegacyTint`, `TerrainLit`); `Enhanced` selects `TerrainLit` and raises `TerrainDecalLimit` to 2000.
2. Share the terrain lighting, cloud, and shadow functions with `TerrainDecal.frag` for `TerrainLit`; sample through the terrain sampler.
3. Add scene `vr-terrain-scorch`: every scorch type on flat and sloped ground, one under a road, one in building shadow, under `faithful` and `enhanced`.
4. Record goldens; show an injected regression (decal pass disabled) fails the gate.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/GraphicsSettings.cs`
- `src/Panopticon.Simulation/Assets/Shaders/TerrainDecal.frag`, shared terrain lighting include
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/vr-terrain-scorch.json` (new), `visual-regression-scenes.json`

## Acceptance criteria

- [ ] `Legacy` output matches the `WI-G11-02` capture.
- [ ] `Enhanced` scorches darken with terrain in shadow instead of keeping a constant tint.
- [ ] Both preset goldens recorded; the injected regression fails.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~TerrainDecal|FullyQualifiedName~GraphicsSettings"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

```sh
TESTHARNESS_VR_UPDATE_GOLDENS=1 dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario VisualRegression
```

## Out of scope / guardrails

- No new decal sources.
- Presentation only: decals use `GameClient.Random`; a determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; mark G11 `Done` in the Track G table.
- Tick G11-4 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
