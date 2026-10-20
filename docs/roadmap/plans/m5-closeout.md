# M5 closeout order of operations

> Part of the [Large-Scale RTS Roadmap](../README.md).
>
> Audience: human + agent.
>
> Execution sequence for the incomplete phases needed to close Track M through M5.
> Planning snapshot: 2026-10-06. Phase definitions, statuses, and dependencies remain
> authoritative in the track documents linked below.

## Scope

M0 is complete. The battle uses the sourced `deepdivide` map; map authoring is outside
this closeout. Completed prerequisites do not appear in the sequence.

This plan schedules existing work without changing phase dependencies or statuses.
Stages are completion gates, not a requirement to serialize independent work.

## Order of operations

| Stage | Incomplete phases | Exit condition |
|---|---|---|
| 1. Foundations | B1, H2a, and A1-6, in parallel. | Reusable benchmarks exist; shared unit-content import supports the bounded EE2 proof; operational recording supports large-run replay. |
| 2. Measurements and content | M1 after B1; M2 after H2a; finish A4's remaining seams/harness after B1. | The 2000-unit probe identifies bottlenecks; EE2 reference units pass validation; experiment seams/harness are complete. |
| 3. Scale and balance | B4 after B1, guided by M1; B5; M3 after M2. | Group pathfinding and rendering meet measured budgets; two asymmetric 1000-unit rosters meet points and duel bounds. |
| 4. AI and replay | M4 after M1/A4, integrating B4 group movement; finish A5 after its supporting recording, harness, and rendering work. | Army AI meets its budget without idle-unit stalls; large recordings load and scrub within bounded memory. |
| 5. Integration and acceptance | M5 after M3, M4, B4, B5, and A5. | The full mixed-roster battle passes batch, replay, and live-view acceptance. |

B5 can start in Stage 1 because A3 is complete. Ready portions of A5 can also start
early. B4 implementation can proceed after B1, with M1 measurements directing the
optimization priorities.

A4-1 through A4-4, including the hierarchy consumed by M4, are complete. The
remaining A4 seams/harness stay in this plan because M4 formally depends on A4,
and A5-2 needs the harness.

## Measurement-driven optimization

After M1, determine whether additional optimization is necessary at 2000 units:

1. Add B2 if allocation or hot-path costs prevent the target.
2. Add B3 after B2 if parallel/data-oriented logic is still required.
3. Repeat the M1 probe after optimization to verify the measured bottleneck is resolved.

B2 and B3 are conditional, not mandatory closeout phases.

## A5 completion sequence

The M5 dependency names all of A5. Preserve that gate rather than silently reducing
it to replay-only functionality.

| Sub-phase | Scheduling |
|---|---|
| A5-1. Scenario authoring | Start with the existing host and run-output foundation. |
| A5-3. Results replay | Start with existing run outputs; integrate A1-6 reconstruction for operational recordings. |
| A5-2. Parameter sweeps | Complete after the remaining A4 harness work. |
| A5-4. Divergence localization | Complete after A5-3, using existing T1/T2 recording. |
| A5-5. Scale support | Integrate B1 measurement, B5 rendering, and bounded-memory large-run replay. |
| A5-6. WorldBuilder baseline | Run independently. Not needed for M's sourced map, but included in full A5 closure. |

Track M does not require B4a or B7. A5's broader design references those tracks;
their integrations are not additional M5 gates. Any reduction of the formal A5
dependency requires a separate roadmap decision.

## M5 acceptance

- Seeded batch battles reach a P3 outcome and report per-side win rates with confidence intervals.
- Mirrored controls show no map bias.
- Finished replays scrub successfully.
- Live viewing sustains a 30 fps median with the battle in view.

## Excluded work

- B4a/B4b map authoring and generation.
- B6 distributed simulation and B7 variable fidelity.
- Track W domain code.
- Optional C4h particle effects.
- Neural-policy extensions beyond the existing rule-based hierarchy.

## References

- [Track M](../track-m-mass-battle.md): phase milestones and direct dependencies.
- [Mass-battle deep dive](../deep-dives/mass-battle-1k.md): recording, scale, and AI integration requirements.
- [Track A](../track-a-foundations.md): remaining host, harness, and visualization work.
- [A5 deep dive](../deep-dives/a5-visualization.md): sub-phase sequencing and integration requirements.
- [Track B](../track-b-scale.md): benchmarking and scaling prerequisites.
- [Track H](../track-h-storage-formats.md): H2a import-layer prerequisite.
