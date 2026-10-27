# Task Card `WI-G11-03`: Logic and map scorch producers

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G11-03` |
| Roadmap phase | [Track G · G11](../../track-g-rendering-clarity.md): Terrain impact decals |
| Status | `Ready` |
| Type | `implementation` |
| Effort | `S` |
| Prerequisites | `WI-G11-01` |
| Diagrams | [G11 architecture](../../deep-dives/g11-terrain-impact-decals.md#5-architecture-diagrams) |

## Goal

Add scorches from the three original logic producers and from map-placed scorch objects.

## Context to read (and only this)

- [G11 design](../../deep-dives/g11-terrain-impact-decals.md) sections 1 and 3 (map-placed scorches).
- `src/Panopticon.Simulation/Logic/Object/Behaviors/NeutronMissileSlowDeathBehavior.cs`, `ParticleUplinkCannonUpdate`, `FirestormDynamicGeometryInfoUpdate`.
- `src/Panopticon.Simulation/Logic/Object/IGameObjectClient.cs`, `Client/GameClient.cs`.
- `src/Panopticon.Simulation/Scene3D.cs` (`LoadObjects`).
- EA release `MapObject.h` (`MO_SCORCH`) and `W3DTerrainVisual.cpp` map scorch load, for reference.

## Steps

1. Add a client entry `AddScorch(position, radius, type)` reachable from logic modules without logic depending on the store.
2. Call it where the three modules already run their scorch timing (neutron missile blast delays, uplink cannon scorch interval, firestorm); modules not yet implemented record the gap in the G11 plan instead.
3. Identify how map files mark scorch objects (original flag and `scorchType`/`objectRadius` properties) and add them in `LoadObjects`; record the maps that carry them.
4. Add tests for the client entry and a map-load test on a synthetic map object.

## Files likely to change

- `src/Panopticon.Simulation/Client/GameClient.cs`, `Logic/Object/IGameObjectClient.cs`
- The three producer modules
- `src/Panopticon.Simulation/Scene3D.cs`
- `test/Panopticon.Simulation.Tests/Terrain/TerrainDecalProducerTests.cs` (new)

## Acceptance criteria

- [ ] Each implemented producer adds decals with the original size and type; unimplemented producers are listed as gaps.
- [ ] Map-placed scorches appear on load and are not double-added on reload.
- [ ] Headless runs (no client) skip decals without errors.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~TerrainDecal"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Runtime decals are not persisted in saves (original behaviour).
- Presentation only: decals use `GameClient.Random`; a determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; if `WI-G11-02` is `Done`, set `WI-G11-04` to `Ready`.
- Tick G11-3 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
