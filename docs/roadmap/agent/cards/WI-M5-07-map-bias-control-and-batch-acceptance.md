# Task Card `WI-M5-07`: Map-bias control and batch acceptance

| Field | Value |
|---|---|
| Card ID | `WI-M5-07` |
| Roadmap phase | [M5](../../track-m-mass-battle.md): 1000 vs 1000 battle |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M5-06` |
| Diagrams | [M5 battle](../../deep-dives/mass-battle-1k.md#m5-battle) |

## Goal

Add the mirror-match control and the side-swap variant, run the acceptance batches, and record
the M5-2 result.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m5-mass-battle.md`: decisions 6 and 7.
- `src/Panopticon.Tools.TestHarness/MassBattleBatchRunner.cs` and `MassBattleBatchReport.cs`.
- `src/Panopticon.Tools.TestHarness/mass-battle-deepdivide-battle.json`.

## Steps

1. Add a `MassBattleBatchVariant` knob: `Battle` (profile as authored), `Mirror` (side A's
   roster on both sides), `Swap` (rosters exchanged between zones). The variant rewrites side
   rosters only; zones, airfields, and seeds stay the same.
2. Time one full-size run per variant and pick the acceptance batch size from the measured wall
   time; record the size and the reason in the M5 plan.
3. Run all three variants at that size and write their batch reports.
4. Add a `Batch results` section to the M5 plan: per variant, runs, wins, draws, failures, win
   rates with intervals, the top kill and loss rows per side, median frames to outcome.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/MassBattleBatchRunner.cs`
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`
- `docs/roadmap/implementation/track-m-m5-mass-battle.md`
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/MassBattleBatchTests.cs`

## Acceptance criteria

- [ ] Every run in all three batches reaches a P3 outcome.
- [ ] The `Mirror` side A interval includes 50 percent; if not, the card is `Blocked` with the
  report and the map-bias finding.
- [ ] The M5 plan records all three batches and the chosen batch size.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="<ee2 install>" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario MassBattleBatch
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="<ee2 install>" \
  dotnet test src --filter "FullyQualifiedName~MassBattleBatch"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No roster rebalancing when the asymmetric battle is lopsided; that is a result.
- No zone or map edits to remove a measured bias; record it and stop.

## On completion

- Set Status above to `Done`.
- Tick M5-2 in the implementation checklist and update the README progress row.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - State: `Blocked` on the full-scale acceptance batches, everything else landed. Variant
    machinery: `MassBattleBatchVariant` knob (`Battle`/`Mirror`/`Swap`) applied by
    `MassBattleBatchRunner.ApplyVariant` (rosters only; the variant profile lands in the run
    directory with an absolute manifest path); the battle profile now gives both zones the same
    2-row 38-anchor grid so a swap can field either roster from either zone (side A previously
    had 25 anchors and could not park the ee2-led air fleet; side A's second row overhangs its
    zone edge by 32 units of footprint). Scaled validation: variant unit test + 1-seed scaled
    Mirror and Swap batches decide with correct rewritten rosters (Interactive 3/3 incl. the
    aggregate reconciliation). Acceptance sizing (decision 7): the measured full-size Battle run
    is 53970 frames in 148.7 min (165 ms/frame, the 54000-frame cap), so an acceptance batch is
    5 seeds per variant (Wilson n=5 excludes 50 percent at 5-0 and 0-5; n<4 cannot exclude it),
    roughly 12.5 hours per variant and 37 hours for all three sequential: operator-scheduled
    overnight batches, commands in the M5 plan. Card stays `Blocked` until the three batch
    reports exist and the Mirror control passes; M5-2 stays unticked in the checklist.
