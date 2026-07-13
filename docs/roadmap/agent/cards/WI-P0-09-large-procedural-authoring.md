# Task Card `WI-P0-09`: Large/procedural authoring: verify model and add scale test

| Field | Value |
|---|---|
| Card ID | `WI-P0-09` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-6, see [deep dive §3.6](../../deep-dives/p0-playable-baseline-harness.md)) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P0-06` (exporter open-command coverage) |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/p0-playable-baseline-harness.md#8-architecture-diagrams) |

## Goal

Confirm that the `ReplayScenario`/`CommandSpec`/`ReplayExporter` stack can handle
programmatically-generated large command sets (≥10k commands) without per-command hand-entry,
within a documented time/memory budget. This is a test-addition card: if the model already
supports it (likely: it is plain C# collections), the card adds the benchmark/regression test and
documents the result; if gaps exist, it surfaces them for a targeted follow-up.

## Context to read (and only this)

- `src/OpenSage.Tools.ReplaySketch.Core/Model/ReplayScenario.cs`: `PlayerSlotConfig.Actions`
  is a `List<ActionEntry>`, populated normally.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ActionEntry.cs`: `ActionEntry` constructor;
  verify it has no hidden per-instance allocation beyond the fields.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs:44-160`: the export loop;
  look for any N² behaviour (nested iteration, repeated LINQ materializations) that would blow
  up at 10k commands.
- `src/OpenSage.Tools.ReplaySketch.Tests/`: test style reference.
- `docs/roadmap/deep-dives/p0-playable-baseline-harness.md §3.6`: intent.

## Steps

1. Read `ReplayExporter.Export` and `PlayerSlotConfig.Actions` for any N² or large-allocation
   patterns at scale. Document findings in the test file as a comment.

2. In `OpenSage.Tools.ReplaySketch.Tests`, add a test
   `GenerateLargeScenario_ExportsWithinBudget`:
   - Build a `ReplayScenario` with one player and 10,000 `ActionEntry` values, each an open
     `CommandSpec` (`OrderType.MoveTo` + one `PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))`)
     with `FixedTiming(1)`.
   - Time the call to `ReplayExporter.Export(...)` using `Stopwatch`.
   - Assert export succeeds (no error), `deferredActions` is empty, output file exists, and elapsed
     time is < 5 seconds.
   - Log the actual elapsed time to `Console.WriteLine` so it appears in test output.

3. If step 1 reveals a real N² problem (e.g., `Actions.FirstOrDefault` inside the timing loop
   with `AfterActionTiming` scanning the whole list on each action), fix it and note the change in
   the test's comment. Otherwise this step is a no-op. Do not over-engineer.

4. Add a second smaller test `GenerateScenario_WithMixedTimings_Exports`:
   - 100 actions: alternating `FixedTiming(10)` and `RandomTiming(5, 20)`.
   - Assert export succeeds with a non-empty output file.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Tests/`: two new tests.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs`: only if a real N² issue
  is found (see step 3).

## Acceptance criteria

- [ ] A 10,000-command scenario exports without error in < 5 seconds on the development machine
      (document the actual time in the test output).
- [ ] `deferredActions` is empty for an all-`FixedTiming` scenario.
- [ ] The output `.rep` file size is > 0 bytes.
- [ ] Mixed-timing test passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src/OpenSage.Tools.ReplaySketch.Tests --logger "console;verbosity=detailed" 2>&1 | grep -E "GenerateLarge|GenerateScenario|Elapsed"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement streaming `.rep` emission or bounded-memory loading: that is A5-5 (scale track).
  P0-6 only ensures the model does not assume small scenarios.
- Do not change `TimingConfig` or the JSON serialization format.
- 10k is the test size; the model should support it. If it doesn't hit the budget, document and
  mark `Blocked` rather than guessing at optimisations.

## On completion

- Set Status above to `Done` (or `Blocked` + reason; include the measured export time).
- Tick the P0-6 large-authoring item in the [implementation checklist](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` including the measured export time and machine spec.
- If WI-P0-08 is also done at this point, update the P0 row in `track-p-playable-baseline.md` to
  `In progress` (all sub-phases landed or in flight).
