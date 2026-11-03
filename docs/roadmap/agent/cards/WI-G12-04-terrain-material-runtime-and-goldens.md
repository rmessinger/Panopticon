# Task Card `WI-G12-04`: Terrain material runtime selection and goldens

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G12-04` |
| Roadmap phase | [Track G · G12](../../track-g-rendering-clarity.md): Open material packs |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-G12-03` |
| Diagrams | [G12 architecture](../../deep-dives/g12-open-material-packs.md#5-architecture-diagrams) |

## Goal

Load replacement terrain colour layers at map load when the operator selects material packs, with safe fallback, and guard the path with G3 goldens.

## Context to read (and only this)

- [G12 design](../../deep-dives/g12-open-material-packs.md) sections 2, 3.3, 4, and the map-load sequence.
- `src/Panopticon.Simulation/Graphics/GraphicsSettings.cs`, `GraphicsSettingsController.cs`, `Game.cs:1079-1082`.
- `src/Panopticon.Simulation/Terrain/Terrain.cs:513-670`.
- `src/Panopticon.Tools.TestHarness/VisualRegressionRunner.cs`, `TerrainZoomProofRunner.cs`; `docs/screenshots.md`.

## Steps

1. Add `TerrainMaterialSource` (`Original`, `Pack`) to `GraphicsSettings` (constructor, `Create`, every `With*` copy, `WithTerrainMaterialSource`), default `Original` in both presets. The controller pins it per session and forces `Original` under `Legacy` with a `Fallbacks` entry.
2. In `Terrain.LoadTerrainTexture`, when the source is `Pack`, try `TerrainMaterialPack.TryLoad(name, arraySize)` first; on miss fall back to the G7 pack, then generation, with one warning per map. Add `ReplacedTextureCount` and `MissingReplacementCount`.
3. Add a G3 capture variant `enhanced-materials` (Enhanced plus `Pack`, root pointed at the synthetic pack) for `terrain-default` and `terrain-grazing`; record goldens. `faithful` and `enhanced` goldens stay unchanged.
4. Add a `TerrainZoomProof` variant with materials on.
5. Operator evidence: Alpine Assault and one desert map, original versus real pack, overview and grazing views, under versioned names per `docs/screenshots.md`.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/GraphicsSettings.cs`, `GraphicsSettingsController.cs`, `Game.cs`
- `src/Panopticon.Simulation/Terrain/Terrain.cs`
- `src/Panopticon.Tools.TestHarness/VisualRegressionRunner.cs`, `TerrainZoomProofRunner.cs`
- `test/Panopticon.Simulation.Tests/Graphics/GraphicsSettings*Tests.cs`, terrain load tests

## Acceptance criteria

- [ ] `Legacy` never opens the material pack root (asserted by test).
- [ ] With the synthetic pack, coverage counts match the map's used entries; removing one layer falls back with one warning.
- [ ] `enhanced-materials` goldens recorded; an injected colour regression fails; existing goldens unchanged.
- [ ] Operator comparison captures shared.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario VisualRegression --headless
```

## Out of scope / guardrails

- Normal and roughness shading (`WI-G12-05`).
- Presentation only; a determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-G12-05` and `WI-G12-07` to `Ready`.
- Tick G12-2 in the checklist and update the README progress row.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
