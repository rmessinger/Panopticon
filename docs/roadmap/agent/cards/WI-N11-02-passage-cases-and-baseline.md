# Task Card `WI-N11-02`: Passage cases and option-off baseline

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N11-02` |
| Roadmap phase | [N11](../../track-n-pathfinding-performance.md): Passage detection and baseline |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `M` |
| Prerequisites | `WI-N11-01`, `WI-N6-02` |
| Diagrams | [Track N passage diagrams](../../deep-dives/n-confined-passage-queueing.md#8-architecture-diagrams) |

## Goal

The `NavigationDeadlock` scenario runs five passage cases and records their option-off baseline, including mouth jams, so N12 to N14 have numbers to beat.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-confined-passage-queueing.md` sections 3 and 4 (N11 case table).
- `src/Panopticon.Tools.TestHarness/NavigationDeadlockRunner.cs`, `NavigationDeadlockCasePlanner.cs`, `NavigationStallSampler.cs` (from `WI-N6-02`).
- `src/Panopticon.Tools.TestHarness/RoutingChokeRunner.cs` (gap setup).

## Steps

1. Add cases `passage-column-40`, `passage-converge-2x20`, `passage-mixed-30`, `passage-opposed-1lane`, and `passage-squad-12` to the case planner, reusing the `RoutingChoke` wall gap.
2. Run `PassageFinder` on each unit's assigned route at order time and report detected passages per case (throat, width, lanes). Detection only.
3. Add the mouth-jam metric to `NavigationStallSampler`: units `BlockedByUnit` within `HoldRadius` of a detected entry, with the longest duration.
4. Record per case: settle frame or `NoProgress`, last arrival frame, mouth-jam units and frames, wait-for cycles, and impassable retrace points.
5. Write the baseline to the plan's results section; revise section 3 targets that the baseline contradicts, with the reason.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/NavigationDeadlockCasePlanner.cs`, `NavigationDeadlockRunner.cs`, `NavigationStallSampler.cs`
- `docs/roadmap/implementation/track-n-pathfinding-performance.md` (results)

## Acceptance criteria

- [ ] Each case detects its gap with the expected lane count or reports why not.
- [ ] Baseline numbers are recorded for all five cases plus `corridor-opposed-20`.
- [ ] Movement is unchanged: existing cases report the same numbers as before this card.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario NavigationDeadlock --headless
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Any queueing behavior (`WI-N12-02`).
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick N11-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
