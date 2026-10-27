# Task Card `WI-N10-01`: Deadlock verification

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N10-01` |
| Roadmap phase | [N10](../../track-n-pathfinding-performance.md): Deadlock verification |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-N7-02`, `WI-N8-02`, `WI-N9-02` |
| Diagrams | [Track N deadlock diagrams](../../deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams) |

## Goal

Verify every deadlock target with all Track N options on, isolate each option's effect, and record a default-flip recommendation.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-navigation-deadlocks.md` sections 3 and 4 (N10).
- `docs/roadmap/implementation/track-n-pathfinding-performance.md` ("WI-N5-01 verification", "Navigation deadlocks results").

## Steps

1. Run every `NavigationDeadlock` case and the M4 army-AI probe with seeds 1, 2, and 3, all options on.
2. Repeat with each of `ValidateOffsetRoutes`, `BlockedUnitResolution`, and `DestinationSlots` off in turn.
3. Report a Met or Missed verdict per section 3 target with numbers, plus the N5 path budgets and M4 invariants.
4. Record the default-flip recommendation for all five Track N options; the operator decides.

## Files likely to change

- `docs/roadmap/implementation/track-n-pathfinding-performance.md`

## Acceptance criteria

- [ ] Every target has a verdict with numbers across 3 seeds.
- [ ] Each M4 run records its stall check result, with per-unit records for any failure.
- [ ] The recommendation is recorded.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src -c Release
dotnet run --project src/Panopticon.Tools.TestHarness -c Release -- --built-in-scenario NavigationDeadlock --headless
dotnet run --project src/Panopticon.Tools.TestHarness -c Release -- --built-in-scenario MassBattleCapacityProbe --headless
```

## Out of scope / guardrails

- No source changes; misses are recorded with their residual cause.
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; update the Track N table Status for N6 to N10.
- Tick N10-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
