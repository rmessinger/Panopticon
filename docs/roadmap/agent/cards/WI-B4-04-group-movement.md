# Task Card `WI-B4-04`: Group movement service

| Field | Value |
|---|---|
| Card ID | `WI-B4-04` |
| Roadmap phase | [B4](../../track-b-scale.md): spatial and pathfinding scaling |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-B4-01` |
| Diagrams | [Group movement sequence](../../deep-dives/b4-spatial-pathfinding-scaling.md#architecture-diagrams) |

## Goal

Move path requests from units to squads: one leader path per group move, followers on
formation offsets, so request counts scale with squads, not units. The service is the seam
M4 and A4-3 consume.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4-spatial-pathfinding-scaling.md` section "Group movement".
- `src/Panopticon.Simulation/Navigation/Navigation.cs` and the move-order path in
  `AIUpdate`.

## Files

- `src/Panopticon.Simulation/Navigation/` group move service
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/` consumption seam
- `test/Panopticon.Simulation.Tests/` group movement tests

## Steps

1. Author the service API: squad descriptor, target, formation offsets; internally one
   `Navigation.CalculatePath` for the leader.
2. Followers track offsets along the leader path each tick; per-unit fallback path only
   when a follower is blocked.
3. Wire the seam so AI-level orders can opt into group movement without changing
   per-unit order semantics.
4. Prove scaling with an install-free or scaled test: N units in squads issue
   squad-count path requests, observable through `PathRequestCount`.
5. Add a scaled scenario check that a squad arrives together at the target.

## Acceptance criteria

- [x] Path requests for a squad move equal squad count, not unit count.
  - `GroupMovementTests`: 5-unit squad issues 1 request; singleton issues 1; aircraft members add
    none (fly direct).
- [x] Followers arrive at formation offsets; blocked followers fall back to per-unit paths.
  - Waypoint-level proof: followers' final target equals destination plus their offset, first
    target equals leader-path node plus offset; a follower whose offset landing is impassable
    issues its own request and targets the exact destination. Live locomotion arrival is deferred
    to the B4-06 scale anchor and M4: mocked-env units carry no locomotor sets, so a headless
    arrival check would not exercise movement (recorded limitation, not faked).
- [x] Per-unit orders behave exactly as before.
  - Singleton-squad test pins per-unit semantics; `AIUpdate.AppendPathToTargetPoint` and
    `SetTargetPoint` are untouched; the service only composes the same public assignment path.
- [x] Seeded determinism suites stay green.
  - `SeededRunTests` + `BaselineGoldenFrames` 3/3; the service has no engine callers yet.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).
  - Full non-interactive suite 2279 passed / 8 failed, the identical known set (repo-standard 7
    plus `ParkingPlaceBehavior_ReportLanding`); format and brace clean.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GroupMove"
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No formation AI beyond offset tracking; M4 owns behaviors.
- No map or roster changes.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- Offset semantics: offsets are applied to the leader path once at order time; per-tick
  formation maintenance stays with M4 behaviors (guardrail).
