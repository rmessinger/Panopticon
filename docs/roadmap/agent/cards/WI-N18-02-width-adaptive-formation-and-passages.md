# Task Card `WI-N18-02`: Width-adaptive formation and passage handoff

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N18-02` |
| Roadmap phase | [N18](../../track-n-pathfinding-performance.md): Group local router |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N18-01`, `WI-N14-01` |
| Diagrams | [Track N layered navigation diagrams](../../deep-dives/n-layered-navigation.md#9-architecture-diagrams) |

## Goal

Groups compress their formation to the corridor width ahead, enter open passages as one block through the passage coordinator, and restore slots after the exit.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-layered-navigation.md` section 5 (N18, width adaptation).
- `docs/roadmap/deep-dives/n-confined-passage-queueing.md` sections 4 (N12, N13).
- `GroupRouter.cs`, `CrossSectionCache.cs`, `PassageFinder.cs`, `PassageCoordinator.cs`.

## Steps

1. Sample cross-section width ahead on the spine every router interval using `CrossSectionCache`.
2. Compress lateral slot extent to `floor(width / pitch)` columns, preserving front-to-back order; restore after the narrow section.
3. When a passage opens on the spine, join the group as a block (the `WI-N13-02` entry) instead of per-member scans; resume slots after the last member exits.
4. Report compression events and block joins in the case report.

## Files likely to change

- `GroupRouter.cs`, `NavigationGroup.cs`, `PassageCoordinator.cs`
- `test/Panopticon.Simulation.Tests/Navigation/GroupRouterTests.cs`

## Acceptance criteria

- [ ] `passage-squad-12` and `layered-detour-wall` settle with 0 waypoints on impassable cells and 0 cycles.
- [ ] Members are never interleaved with another group inside a passage.
- [ ] N14 passage targets still met with `GroupRouting` on.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario NavigationDeadlock --headless
```

## Out of scope / guardrails

- Changes to passage ranking or grants (N12, N13).
- Options stay default off; determinism-test failures follow the suspected-deviation protocol in `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-N20-01` to `Ready` if `WI-N18-03` and `WI-N19-02` are done.
- Tick N18-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
