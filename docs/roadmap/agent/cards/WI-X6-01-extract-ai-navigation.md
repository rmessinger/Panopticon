# Task Card `WI-X6-01`: Extract navigation logic from AIUpdate

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X6-01` |
| Roadmap phase | [X6](../../track-x-fixes-tweaks.md): AIUpdate navigation extraction |
| Status | `Ready` |
| Type | `refactor` |
| Effort | `M` |
| Prerequisites | `WI-N11-01` (the Track N stack must be merged first, so the extraction does not rebase over N6 to N11 edits) |
| Diagrams | [X6 architecture diagrams](../../implementation/track-x-x6-aiupdate-navigation-extraction.md#architecture-diagrams) |

## Goal

Move path request, route application, blocked-movement resolution, and give-way logic out of `AIUpdate` into a dedicated per-unit navigation class, with no behavior change. `AIUpdate` keeps order handling, state-machine glue, turrets, and persistence entry points.

## Context to read (and only this)

- `docs/roadmap/implementation/track-x-x6-aiupdate-navigation-extraction.md`.
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdate.cs`: navigation methods listed in the plan inventory, `Update`, `Load`.
- `src/Panopticon.Simulation/Navigation/BlockedMovementResolver.cs`, `GiveWayPlanner.cs`, `MovementProgress.cs`, `GroupMovement.cs`: the callers of the extracted members.
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdateStateMachine.cs` and the `*AIUpdate.cs` subclasses: callers through `AIUpdate`.
- `docs/roadmap/deep-dives/n-navigation-deadlocks.md`: code-facts list.

## Steps

1. Inventory: list every navigation member and field of `AIUpdate` (path request and pending-handle fields, `TargetPoints`, `Progress`, give-way and local-repath state, route application, settle methods) and every external caller. Record the table in the plan before moving code.
2. Add `UnitNavigation` (one per `AIUpdate`, created in the constructor) holding the navigation fields and methods. It takes the owning `GameObject`, the locomotor access it needs, and callbacks or an interface for the few effects that stay in `AIUpdate` (stop, arrive, model-condition flags). No back-reference to the whole `AIUpdate`.
3. Move members in small commits: path requests and pending handle first, then route application and settle methods, then give-way and local-repath state. Keep `internal` call sites compiling by forwarding from `AIUpdate` only when subclasses or state-machine states call them; remove each forwarder once callers switch to `UnitNavigation`.
4. Keep `TargetPoints` and `Progress` reachable at their current public paths until callers move, then expose them through `AIUpdate.Navigation`.
5. Keep save format identical: the persisted field order and types in `AIUpdate.Load`/`Persist` do not change. `UnitNavigation` exposes a `Persist(StatePersister)` that `AIUpdate` calls at the same position the fields were read and written before.
6. Update `n-navigation-deadlocks.md` code facts and the `BlockedMovementResolver`/`GiveWayPlanner` doc comments that name `AIUpdate` members.
7. Record before/after line counts and the final public surface in "WI-X6-01 results".

## Files likely to change

- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdate.cs`
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/UnitNavigation.cs` (new)
- `src/Panopticon.Simulation/Navigation/BlockedMovementResolver.cs`
- `src/Panopticon.Simulation/Navigation/GiveWayPlanner.cs`
- `src/Panopticon.Simulation/Navigation/GroupMovement.cs`
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/*AIUpdate.cs` (call sites only)
- `test/Panopticon.Simulation.Tests/Navigation/*` (call sites only)
- `docs/roadmap/deep-dives/n-navigation-deadlocks.md`
- `docs/roadmap/implementation/track-x-x6-aiupdate-navigation-extraction.md`

## Acceptance criteria

- [ ] `AIUpdate` contains no path request, pending-handle, route application, give-way, or local-repath logic; it delegates to `UnitNavigation`.
- [ ] `UnitNavigation` has no reference to `AIUpdate`.
- [ ] Load and persist order is unchanged; an existing save round-trips byte-identically.
- [ ] No behavior change: `SeededRunTests`, golden baselines, and run-output byte-identity pass unmodified. A failure in any of them is a suspected deviation to report, not to fix by regenerating.
- [ ] `NavigationDeadlock` all-cases report at seed 1 matches the pre-refactor report field for field.
- [ ] Navigation test call sites updated; no test logic changes.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Panopticon.Tests.Navigation"
dotnet test src --filter Category!=Interactive
dotnet run --project src/Panopticon.Tools.TestHarness -c Release -- --built-in-scenario NavigationDeadlock --headless --n6-blocked-resolution true --n6-validate-offsets true --n6-report /tmp/x6-after.json
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

Run the `NavigationDeadlock` command on the base commit first and write `/tmp/x6-before.json`; compare the two reports.

## Out of scope / guardrails

- No change to pathfinding, locomotion, resolver thresholds, options, or defaults.
- No change to order handling, attack-move, turret, or state-machine behavior.
- No change to the `Navigation` class or its algorithms.
- Pre-existing failures (`ParkingPlaceBehaviorTests.ParkingPlaceBehavior_ReportLanding`, flaky `AsyncPathRequestTests.PendingHandle_SecondRequestReplacesTheFirst`) are recorded, not fixed here.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, tick X6-1 in `docs/roadmap/implementation-checklist.md`, set X6 `Done` in the track doc, update the README progress row, and fill "WI-X6-01 results" in the X6 plan.
- Append a result line to `.exchange`.
