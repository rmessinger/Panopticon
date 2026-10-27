# Task Card `WI-G11-01`: Terrain decal store and scorch FX execution

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G11-01` |
| Roadmap phase | [Track G · G11](../../track-g-rendering-clarity.md): Terrain impact decals |
| Status | `Done` |
| Type | `implementation` |
| Effort | `S` |
| Prerequisites | `WI-G2-03` |
| Diagrams | [G11 architecture](../../deep-dives/g11-terrain-impact-decals.md#5-architecture-diagrams) |

## Goal

Give `TerrainScorch` FX nuggets a working `Execute` that records scorch decals in a client-side, capped `TerrainDecalStore`. No rendering yet.

## Context to read (and only this)

- [G11 design](../../deep-dives/g11-terrain-impact-decals.md) sections 1 to 3 and the class diagram.
- `src/Panopticon.Simulation/FX/FXNuggets/TerrainScorchFXNugget.cs`, `FX/TerrainScorchType.cs`, `FX/FXListExecutionContext.cs`, `FX/FXNuggets/ParticleSystemFXNugget.cs`.
- `src/Panopticon.Simulation/Client/GameClient.cs`, `Terrain/Terrain.cs`.
- `src/Panopticon.Simulation/Graphics/GraphicsSettings.cs`.

## Steps

1. Complete `TerrainScorchType` (`SCORCH_1`..`SCORCH_4`, `SHADOW_SCORCH`, `RANDOM`) with original numeric order; parse `Radius` as a float.
2. Add `TerrainDecalTemplate`, `TerrainDecal`, and `TerrainDecalStore` on `Terrain`: oldest-first eviction past the limit, the original `radius / 4` duplicate rule per template and cell, a `Version` counter, and `Clear` on map unload.
3. Add `GraphicsSettings.TerrainDecalLimit` (`Legacy` and `Enhanced` both 500 here; `WI-G11-04` revisits `Enhanced`).
4. Implement `TerrainScorchFXNugget.Execute`: resolve `RANDOM` to `SCORCH_1`..`SCORCH_4` with `GameClient.Random`, add a `Scorch` template decal at the context position.
5. Add a data test over the installed Generals and Zero Hour INI: every `TerrainScorch` nugget parses to a supported type; record exact nugget counts in the G11 plan.

## Files likely to change

- `src/Panopticon.Simulation/FX/FXNuggets/TerrainScorchFXNugget.cs`, `FX/TerrainScorchType.cs`
- `src/Panopticon.Simulation/Terrain/Decals/TerrainDecalStore.cs`, `TerrainDecal.cs`, `TerrainDecalTemplate.cs` (new)
- `src/Panopticon.Simulation/Terrain/Terrain.cs`, `Graphics/GraphicsSettings.cs`
- `test/Panopticon.Simulation.Tests/Terrain/TerrainDecalStoreTests.cs`, `FX/TerrainScorchDataTests.cs` (new)

## Acceptance criteria

- [x] Store tests cover eviction order, duplicate rejection, version bumps, and clear.
- [x] `RANDOM` resolution draws from client random only (logic random sequence unchanged in a test).
- [x] The data test passes for both games and the counts are recorded.
- [ ] Full standard validation pending group closeout; focused tests pass.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~TerrainDecal|FullyQualifiedName~TerrainScorch"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No mesh or draw (`WI-G11-02`).
- No logic-module producers or map scorches (`WI-G11-03`).
- Presentation only: decals use `GameClient.Random`; a determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-G11-02` and `WI-G11-03` to `Ready`.
- Tick G11-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
