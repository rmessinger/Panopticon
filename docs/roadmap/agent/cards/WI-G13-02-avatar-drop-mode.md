# Task Card `WI-G13-02`: Avatar drop mode

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G13-02` |
| Roadmap phase | [Track G · G13](../../track-g-rendering-clarity.md): First-person ground view |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-G13-01` |
| Diagrams | [G13 architecture](../../deep-dives/g13-first-person-ground-view.md#5-architecture-diagrams) |

## Goal

Pressing the hotkey in RTS mode shows a map-pin avatar marker on the cursor that validates the ground under it; a click on a valid point enters first person there.

## Context to read (and only this)

- [G13 design](../../deep-dives/g13-first-person-ground-view.md) sections 3.2 and the drop sequence.
- `src/Panopticon.Simulation/Logic/OrderGeneratorSystem.cs`, `Logic/OrderGenerators/IOrderGenerator.cs`, `ConstructBuildingOrderGenerator.cs`.
- `src/Panopticon.Simulation/Navigation/Navigation.cs`, `Terrain/Terrain.cs` (`ImpassableAt`), `Logic/PartitionCellManager.cs`, `Logic/Object/Colliders.cs`.

## Steps

1. Add `AvatarDropOrderGenerator : IOrderGenerator`: terrain ray-cast in `UpdatePosition`, `TryActivate` calls `ViewModeController.EnterFirstPerson(point)` on a valid point, right click or `Escape` cancels.
2. Add `AvatarDropValidator`: playable area, `Navigation.IsPassable`, not water, slope within `MaxWalkSlope` from `HeightMap.GetNormal`, clear of structure `RoughCollider`s expanded by the avatar radius. Return a reason enum for invalid points.
3. Add the client-only pin marker mesh (built in code, no asset), green when valid and red otherwise, drawn through `BuildRenderList`; the cursor is hidden while the marker shows.
4. Tests: validator per reason on a synthetic height map and collider set; cancel restores `Tactical` without camera change.
5. Capture valid and invalid marker frames under versioned names per `docs/screenshots.md`.

## Files likely to change

- `src/Panopticon.Simulation/ViewModes/AvatarDropOrderGenerator.cs`, `AvatarDropValidator.cs`, `AvatarMarkerMesh.cs` (new), `ViewModeController.cs`
- `test/Panopticon.Simulation.Tests/ViewModes/AvatarDropValidatorTests.cs` (new)

## Acceptance criteria

- [ ] Hotkey shows the marker; it turns red over cliffs, water, impassable cells, and structures.
- [ ] Click on a valid point enters first person at that point; cancel leaves the camera unchanged.
- [ ] Captures shared.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Walking and transitions (`WI-G13-03`, `WI-G13-05`).
- Presentation only: no `GameObject`, order, or logic random. A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; if `WI-G13-03` is `Done`, set `WI-G13-04` to `Ready`.
- Tick G13-2 in the checklist and update the README progress row.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
