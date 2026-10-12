# Task Card `WI-B5-05`: Scale-anchor budget verification and evidence

| Field | Value |
|---|---|
| Card ID | `WI-B5-05` |
| Roadmap phase | [Track B · B5](../../track-b-scale.md): B5-5 budget proof |
| Status | `Blocked` (flips to `Ready` when `WI-B5-04` is `Done`) |
| Type | `investigation` (measurement runs + documentation) |
| Effort | `S` |
| Prerequisites | `WI-B5-04` |
| Diagrams | not applicable (measurement card; diagrams already grounded) |

## Goal

Run the B5 verification matrix at the scale anchor, record final numbers and honest gaps in
the deep dive, and produce the milestone evidence screenshot. This card completes phase B5.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b5-rendering-at-scale.md`: sections 4, 7, 8.
- `src/Panopticon.Tools.TestHarness/RenderingScaleProbeRunner.cs` (from `WI-B5-01`) and its
  options.
- `docs/screenshots.md`: artifact placement under `/mnt/pit2/Network Storage/temp/`.

## Steps

1. Verification matrix, each a probe run on this host (Enhanced preset, 1024x768 unless a
   run varies it): 10k rigid repeated models with (a) instancing off + LOD off (baseline
   recheck), (b) instancing on + LOD off, (c) instancing on + LOD on, (d) Legacy shadow
   tier, (e) 1k and 30k rigid for scaling shape; plus the anchor composition (10k skinned
   Rangers) to record the non-instanced gap.
2. Fill deep dive §7 after-B5 columns: frame median/p95, render span, draw calls, batches,
   instances, fps vs 33.3 ms budget, and the skinned gap paragraph.
3. Compare against the budget table in §4: mark each budget met or missed with the number;
   no tuning to flatter numbers.
4. Write the milestone screenshot (10k instanced scene, one shared artifact, unique filename)
   to `/mnt/pit2/Network Storage/temp/` top level per `docs/screenshots.md`.
5. Update the B5 rows: track doc Status to Done, checklist B5 item ticked with its AC, this
   card Done, `.exchange` entry.

## Files likely to change

- `docs/roadmap/deep-dives/b5-rendering-at-scale.md` (§4/§7 numbers)
- `docs/roadmap/track-b-scale.md` (B5 Status)
- `docs/roadmap/implementation-checklist.md` (B5 item tick)

## Acceptance criteria

- [ ] Every matrix cell has a recorded number in §7 (median + p95 frame ms, draws,
      batches/instances).
- [ ] Budget verdicts stated explicitly: met or missed with the measurement.
- [ ] Skinned-mesh gap recorded with its measured frame cost at 10k.
- [ ] Milestone screenshot in the shared temp directory.
- [ ] Standard validation passes (no source change expected; run to confirm).

## Validation commands

```sh
dotnet build src
dotnet test src --filter 'Category!=Interactive'
# Matrix runs: RenderingScaleProbe with the option switches from WI-B5-01..04
```

## Out of scope / guardrails

- No source changes to improve numbers in this card; a miss is recorded, not patched.
  If a fix is obvious, it becomes a follow-up card proposal in `.exchange`, not an edit here.
- Determinism-enforcing failures (golden frames, seeded runs) are suspected deviations:
  stop and report per the repo rules.

## On completion

- Set Status to `Done`.
- Set the B5 phase Status in `track-b-scale.md` to `Done` and tick the checklist item.
- Append a `.exchange` line with any follow-up card proposals (skinned instancing,
  impostors, NormalMapped/Simple variants).
