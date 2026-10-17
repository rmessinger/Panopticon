# Task Card `WI-M4-06`: AI cost budget and stall report

| Field | Value |
|---|---|
| Card ID | `WI-M4-06` |
| Roadmap phase | [M4](../../track-m-mass-battle.md): army-scale AI |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-M4-04`, `WI-M4-05` |
| Diagrams | none (report over measured runs) |

## Goal

Author the per-tick AI cost budget and the idle-stall threshold, measure both on a full-scale M1 army-AI run, and record the verdict (M4-3); close the M4 phase.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m1-capacity-probe.md`: tick budget and baseline spans.
- `docs/roadmap/implementation/track-m-m4-army-ai.md`.
- The army-AI run's B1 record and engagement report.

## Steps

1. Author the budget: `system.ai.spanMs` limit on decision ticks and amortized per tick, stated against the 33.3 ms frame anchor; author the stall threshold in frames.
2. Add idle-stall detection to the army-AI report: a live unit with no target point, no weapon target, and no movement for longer than the threshold while enemies remain.
3. Run the full-scale army-AI probe headless; record `ai` span medians and p99, path requests per decision tick, assignment invariants, mission counts, and stalls.
4. Write the verdict section in the M4 implementation plan; tick checklist M4-1..M4-3 and set the M4 phase status in the track doc.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs` (stall detection)
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeReport.cs`
- `docs/roadmap/implementation/track-m-m4-army-ai.md`
- `docs/roadmap/implementation-checklist.md`
- `docs/roadmap/track-m-mass-battle.md`

## Acceptance criteria

- [ ] Budget and stall threshold are authored with their reasoning.
- [ ] The B1 record shows AI decision cost against the budget, with medians and p99.
- [ ] No unit idles longer than the stall threshold while enemies remain, or each stall is listed as an explicit failure.
- [ ] Checklist M4-1..M4-3 ticked or annotated; M4 phase status updated.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness --no-build -- \
  --headless --built-in-scenario MassBattleCapacityProbe   # army-AI mode per WI-M4-03
```

## Out of scope / guardrails

- A missed budget is recorded as a result, not fixed here; fixes become follow-up cards.
- Do not cite headless tick rate as windowed frame rate.

## On completion

- Set Status above to `Done`.
- Update the M4 phase Status in the track doc.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - budget-miss fixes, if any.
