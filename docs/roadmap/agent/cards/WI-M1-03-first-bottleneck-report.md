# Task Card `WI-M1-03`: First-bottleneck report

| Field | Value |
|---|---|
| Card ID | `WI-M1-03` |
| Roadmap phase | [M1](../../track-m-mass-battle.md): capacity probe |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-M1-02` |
| Diagrams | none (report over measured runs) |

## Goal

Run the measured 1000 vs 1000 probe headless and windowed, read the B1 records, and write the first-bottleneck report: which constraint fails first at 2000 units, how far the engine is from M5's needs, and whether B2 (frame cost reduction) or B3 (parallel storage/simulation) is required for M5 or can be skipped.

## Context to read (and only this)

- `docs/roadmap/deep-dives/mass-battle-1k.md` section 7: scale dependencies table (what must hold at 2000 units).
- `docs/roadmap/plans/m5-closeout.md`: stage 2 exit criteria and the B2/B3 decision handed to this report.
- The probe's B1 records and engagement reports from WI-M1-02.

## Steps

1. Run the full probe headless and windowed on the operator machine; keep the produced records.
2. Analyze: tick wall ms over the engagement (mean/median, tail), frame rate vs the M5 live-view 30 fps target, memory growth and GC pressure, path requests per second, and system span split (gameLogic, network, scene3d, partition).
3. Name the first failing constraint: the quantity that breaks its budget first as the armies close and engage, with the frame ranges and numbers that show it.
4. State the B2/B3 verdict for M5 with the measured reasoning; record follow-ups (for example a counter or probe that M4 or M5 will need).
5. Write `docs/roadmap/implementation/track-m-m1-capacity-probe.md` with the tables and verdict; tick M1-1..M1-3 in `implementation-checklist.md` and set the M1 phase status in `track-m-mass-battle.md`.

## Files likely to change

- `docs/roadmap/implementation/track-m-m1-capacity-probe.md` (new)
- `docs/roadmap/implementation-checklist.md`
- `docs/roadmap/track-m-mass-battle.md`

## Acceptance criteria

- [ ] The report names the first failing constraint with measured numbers and frame ranges from the B1 records.
- [ ] The report states whether B2 and B3 are needed for M5, with reasoning a reviewer can check against the records.
- [ ] Headless and windowed records are both cited (tick cost from headless, frame rate from windowed).
- [ ] Checklist M1-1..M1-3 ticked; M1 phase status updated in the track doc.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario MassBattleCapacityProbe
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No optimization work: M1 measures and names; fixes belong to B2/B3 or later phases.
- No extrapolation beyond one doubling (2000 units) without labeling it as extrapolation.

## On completion

- Set Status above to `Done`.
- Update the roadmap phase Status if this card completes it.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - record any B2/B3 prerequisite cards the verdict demands.
