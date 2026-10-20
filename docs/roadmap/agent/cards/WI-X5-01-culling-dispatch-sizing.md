# Task Card `WI-X5-01`: Right-size parallel culling dispatch

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X5-01` |
| Roadmap phase | [X5](../../track-x-fixes-tweaks.md): Render culling dispatch |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | M1 (`MassBattleCapacityProbe`) |
| Diagrams | [X5 architecture diagrams](../../implementation/track-x-x5-render-culling-dispatch.md#architecture-diagrams) |

## Goal

Measure what per-pass frustum culling costs in the windowed capacity probe, then make `RenderItemCollection.FilterVisible` run serially below a measured item threshold and in processor-sized chunks above it, with identical visible lists.

## Context to read (and only this)

- `docs/roadmap/implementation/track-x-x5-render-culling-dispatch.md`.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderItemCollection.cs`.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`: `ParallelCullingBatchSize`, `DoRenderPass`, `RenderSpan`.
- `test/Panopticon.Simulation.Tests/Graphics/Rendering/RenderItemCollectionSortFilterTests.cs`.
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`: windowed step and B1 record.
- `docs/roadmap/agent/cards/WI-X2-01-bound-upload-resource-lifetimes.md`: before/after evidence format for this probe.

## Steps

1. Baseline: run the windowed `MassBattleCapacityProbe` (Xvfb on Linux) under `dotnet-trace collect --profile cpu-sampling`. Report the inclusive and exclusive CPU share of `FilterVisible`, `Parallel.ForEach` workers, and thread-pool spin/wait frames, plus the per-frame `shadowPass` and `forwardPass` medians from the B1 record. If culling plus dispatch is under 1% of frame CPU, record the numbers in the plan, set the card `Done` with "no change needed", and stop.
2. Micro-benchmark `FilterVisible` serial versus the current 128-item parallel path at 100, 1k, 10k, and 60k items on this host. Pick `MinParallelItems` as the smallest count where parallel is faster beyond the CI, and `MinChunk` from the same run.
3. Add `CullingDispatch` (record struct with `MinParallelItems`, `MinChunk`, `ChunkSize(count, processorCount)`, and a `Serial` instance) and replace the `int batchSize` parameter of `FilterVisible`. Remove `ParallelCullingBatchSize` and its TODO.
4. Add tests: serial and parallel dispatch produce identical `CulledItemIndices` with and without clipping planes, for counts below and above the threshold; `ChunkSize` never returns less than `MinChunk` or more than `count`.
5. Rerun step 1 with the same profile and frame budget. Record before/after CPU shares and `shadowPass`/`forwardPass` medians with 95% CIs in "WI-X5-01 results".

## Files likely to change

- `src/Panopticon.Simulation/Graphics/Rendering/RenderItemCollection.cs`
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`
- `src/Panopticon.Simulation/Graphics/Rendering/CullingDispatch.cs` (new)
- `test/Panopticon.Simulation.Tests/Graphics/Rendering/RenderItemCollectionSortFilterTests.cs`
- `docs/roadmap/implementation/track-x-x5-render-culling-dispatch.md`

## Acceptance criteria

- [ ] Baseline CPU-sampling profile attributes culling, worker, and spin/wait cost in the windowed probe.
- [ ] Threshold and chunk values come from the recorded micro-benchmark.
- [ ] Serial and parallel dispatch produce identical visible lists in tests.
- [ ] Before/after CPU shares and `shadowPass`/`forwardPass` medians with CIs are recorded; a result inside noise is recorded as such.
- [ ] Probe battle results are unchanged between runs (rendering only).
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~RenderItemCollection"
DISPLAY=:99 CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet-trace collect --profile cpu-sampling -o /tmp/x5-before.nettrace -- \
  dotnet run --project src/Panopticon.Tools.TestHarness --no-build -- --built-in-scenario MassBattleCapacityProbe
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No change to cull results, sort order, render keys, or pass structure.
- No reuse of main-camera visibility for water re-passes, shadow culling redesign, or render-list construction work (B5 follow-up).
- No change to logic, determinism baselines, or headless runs.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, tick X5-1 in `docs/roadmap/implementation-checklist.md`, set X5 `Done` in the track doc, update the README progress row, and fill "WI-X5-01 results" in the X5 plan.
- Append a result line to `.exchange`.
