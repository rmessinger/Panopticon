# Implementation Plan: Track P · P0-6: Procedural and Programmatic Command Authoring

> Track: [Track P: Playable Baseline](../track-p-playable-baseline.md)
> Phase: [P0: ReplaySketch arbitrary-command harness](../deep-dives/p0-playable-baseline-harness.md), sub-phase P0-6: Procedural authoring at scale
> Checklist item: [implementation-checklist.md](../implementation-checklist.md), P0 item "Support procedural/programmatic authoring of large command sets with efficient .rep emission"
> Related: full authoring-tool scale is A5-5 ([visualization.md](../deep-dives/a5-visualization.md)); this is the minimal P0 slice.
>
> Audience: human overview of the implementation plan. Agent context sits below the divider.

## Problem

Today commands are authored one-by-one through the MAUI UI / `ActionEntry` list, which does not scale
to the order volumes the rest of Track P needs (a full skirmish, then the 10k-unit scale anchor from
the settled scale Decision). There is no programmatic way to generate thousands of `CommandSpec`s and
emit them to a `.rep` within a known time/memory budget. Without this, P4/P6/P7 stress scenarios and
B-track benchmarks cannot be authored at all.

Checklist item AC:
- A programmatic API generates large command sets (not hand-entry) and emits a valid `.rep`.
- A documented budget: ≥10k generated orders export and load within a stated time/memory bound,
  exercised by a test.

## Design

A code-first authoring API plus a throughput check, reusing the landed open command model.

### Authoring API (Core)

- `ScenarioBuilder` : a fluent/programmatic builder producing a `ReplayScenario`:
  - `builder.At(frame).Command(CommandSpec)` / `builder.At(frame).ForPlayer(i).Command(...)`;
  - convenience generators that emit many `CommandSpec`s over a frame range (e.g. "spawn N units of
    def D for player P across frames F0..F1"), all funnelling into the existing
    `CommandSpec` (`OrderType` + `CommandArgument[]`) so no new order plumbing is needed;
  - accepts an explicit `seed` as a procedural-generation input (the harness already threads `rng`
    into `CommandSpec.ToOrder`).
- Output is an ordinary `ReplayScenario`, so it flows through the existing `ReplayExporter` and the
  [run-and-assert entry point](track-p-p0-6-run-and-assert-entry-point.md) unchanged.

### Efficient `.rep` emission

- Audit `ReplayExporter.BuildOrders`/`Export` (`:200-269`) for per-order allocations and ensure the
  write path streams to the `.rep` rather than buffering intermediate copies; size the order list up
  front when the count is known. The goal is linear-time, low-allocation emission for 10k+ orders.
- Keep the file-format output byte-identical to the current single-order path (same `Replay`/`Order`
  encoding) so existing replays and the engine's `ReplayConnection` keep working.

### Budget + measurement

- Add a test (`Category!=Interactive`) that generates ≥10k orders via `ScenarioBuilder`, exports to a
  temp `.rep`, reloads it, and asserts the order count round-trips. Record elapsed time and peak
  managed allocation; assert they stay under a documented bound (the bound is written into the P0 deep
  dive so regressions are visible). This reuses the B1 benchmarking discipline at a tiny scale; the
  real benchmark harness is B1.

---

> Agent implementation details : the sections below list exact files, methods, and
> tests for agents executing this plan. Human readers can stop here.

## Files to change

| File | Change |
|---|---|
| `src/Panopticon.Tools.ReplaySketch.Core/Authoring/ScenarioBuilder.cs` | NEW : programmatic builder + bulk generators (seeded) |
| `src/Panopticon.Tools.ReplaySketch.Core/Services/ReplayExporter.cs` | Streamlined large-set emission (preallocate, stream, no redundant copies); behaviour-preserving |
| `docs/roadmap/deep-dives/p0-playable-baseline-harness.md` | Document the authoring API and the export/load budget |
| `test/Panopticon.Tools.ReplaySketch.Tests/ScenarioBuilderTests.cs` | Unit tests for the builder (Core-only project) |
| `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/ReplaySketchScaleTests.cs` | NEW : 10k-order export/load round-trip + budget assertion |

## Tests

- Builder: a small generated scenario produces the expected `CommandSpec` count, frames, and player
  attribution.
- Round-trip: ≥10k generated orders export to `.rep` and reload with the order count preserved.
- Budget: export+load of the 10k set completes within the documented time/allocation bound.

## Out of scope

- UI for procedural authoring; this is a code-first API (MAUI stays for small hand-authoring).
- The full authoring/visualization tool scaling to the 10k-unit anchor → A5-5
  ([visualization.md](../deep-dives/a5-visualization.md)).
- The rigorous, statistical benchmark harness → B1 ([track-b-scale.md](../track-b-scale.md)); this
  plan ships only a single guardrail budget test.
