# Task Card `WI-G13-03`: Avatar ground locomotion

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G13-03` |
| Roadmap phase | [Track G · G13](../../track-g-rendering-clarity.md): First-person ground view |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-G13-01` |
| Diagrams | [G13 architecture](../../deep-dives/g13-first-person-ground-view.md#5-architecture-diagrams) |

## Goal

Walk the avatar over terrain in first person with mouse look, blocked by slopes, impassable cells, structures, and the map border.

## Context to read (and only this)

- [G13 design](../../deep-dives/g13-first-person-ground-view.md) sections 2 (scale) and 3.3.
- `src/Panopticon.Simulation/Terrain/HeightMap.cs`, `Terrain/TerrainLogic.cs`, `Terrain/Terrain.cs` (`ImpassableAt`), `Logic/PartitionCellManager.cs`, `Logic/Object/Colliders.cs`.
- [G10 reference scale](../../deep-dives/g10-realistic-proportions.md).

## Steps

1. Add `AvatarState`, `AvatarInput`, `IGroundQuery`, and pure `AvatarLocomotion.Step` with a fixed client substep: walk and run speeds, pitch clamp ±85°, eye smoothing over steps, slope and impassable blocking with edge sliding, structure cylinders, border clamp, gravity past `MaxStepDown`.
2. Derive `EyeHeight` (0.93 of the Ranger model height), avatar radius, and walk speed (1.4 m/s equivalent) from the G10 reference scale; record the values.
3. Add `TerrainGroundQuery` over `HeightMap`, `Terrain.ImpassableAt`, and `PartitionCellManager.QueryObjects` structure colliders.
4. Drive `FirstPersonCameraController` from WASD, `Shift`, and `MouseRelativeMove`; it calls `Camera.SetLookAt` each frame.
5. Tests per behaviour in step 1 on synthetic terrain; a headless run of 600 frames with scripted input asserts unchanged simulation assertions.

## Files likely to change

- `src/Panopticon.Simulation/ViewModes/AvatarState.cs`, `AvatarLocomotion.cs`, `IGroundQuery.cs`, `TerrainGroundQuery.cs` (new), `FirstPersonCameraController.cs`
- `test/Panopticon.Simulation.Tests/ViewModes/AvatarLocomotionTests.cs` (new)

## Acceptance criteria

- [ ] Locomotion tests pass for flat walk, run, slope block, impassable slide, structure block, cliff fall, and border clamp.
- [ ] Scale values derive from G10 and are recorded in the plan.
- [ ] Units neither block nor are affected by the avatar.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~AvatarLocomotion"
```

## Out of scope / guardrails

- Jumping, crouching, swimming, bridges (layers are stubbed), unit collision.
- Presentation only: no `GameObject`, order, or logic random. A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; if `WI-G13-02` is `Done`, set `WI-G13-04` to `Ready`.
- Tick G13-3 in the checklist and update the README progress row.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
