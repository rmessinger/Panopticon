# Task Card `WI-M5-10`: Live frame rate measurement

| Field | Value |
|---|---|
| Card ID | `WI-M5-10` |
| Roadmap phase | [M5](../../track-m-mass-battle.md): 1000 vs 1000 battle |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-M5-09` |
| Diagrams | [M5 battle](../../deep-dives/mass-battle-1k.md#m5-battle) |

## Goal

Measure the live spectator battle through B1 on the operator's machine against the 30 fps median
milestone, attribute the frame time, and record the verdict and follow-ups.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m5-mass-battle.md`: decision 9 and the starting gaps.
- `docs/roadmap/implementation/track-m-m1-capacity-probe.md` and
  `docs/roadmap/implementation/track-m-m4-army-ai.md`: measured headless baselines.
- `docs/roadmap/implementation/track-b-b5-rendering-scale.md`: rendering-at-scale results.
- `src/Panopticon.Benchmarks.Simulation/BenchmarkMetricRecorder.cs`: windowed `tick.fps` and spans.

## Steps

1. Run the windowed spectator battle on a presenting display (operator machine), with the full
   battle in view, seed 1, to outcome or 9000 measured frames.
2. Record the B1 report: median and p95 `tick.fps` and `tick.wallMs`, the logic, `ai`,
   partition, and presentation spans, units alive over time, and the hardware context.
3. Add a `Live frame rate` section to the M5 plan: the numbers, the largest spans, and a met or
   missed verdict.
4. On a miss, write follow-up cards in the owning track (B2 allocation, B3 parallel logic, B5
   rendering) with the measured span as their baseline.

## Files likely to change

- `docs/roadmap/implementation/track-m-m5-mass-battle.md`
- Follow-up cards under `docs/roadmap/agent/cards/` (on a miss)

## Acceptance criteria

- [ ] A B1 record from a presenting windowed run is committed or linked from the M5 plan.
- [ ] The verdict states met or missed against a 30 fps median, with the attributed spans.
- [ ] Each miss cause has a follow-up card or a stated reason for none.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="<ee2 install>" \
  dotnet run -c Release --project src/Panopticon.Tools.TestHarness -- --built-in-scenario MassBattle
```

## Out of scope / guardrails

- No engine optimization in this card.
- Xvfb or software rendering numbers do not count toward the milestone.

## On completion

- Set Status above to `Done`.
- On a met verdict, tick M5-4 in the implementation checklist and update the README progress row.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
