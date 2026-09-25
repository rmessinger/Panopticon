# Task Card `WI-P9-06`: Timing report output

| Field | Value |
|---|---|
| Card ID | `WI-P9-06` |
| Roadmap phase | [Track P · P9](../../track-p-playable-baseline.md): P9-6 Report output, see [deep dive §2.6](../../deep-dives/p9-unit-validation-ground.md#26-gaps-rather-than-workarounds) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P9-04`, `WI-P9-05` |
| Diagrams | [P9 Architecture Diagrams](../../deep-dives/p9-unit-validation-ground.md#6-architecture-diagrams) |

## Goal

Emit the validation-ground results as a readable stdout table and a machine-readable JSON artifact,
reporting gaps and timeouts distinctly from measured durations.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p9-unit-validation-ground.md`, §2.5 (timeout versus regression) and §2.6.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ValidationGroundReport.cs`: the report shape from
  `WI-P9-04`.
- `src/Panopticon.Tools.TestHarness/ActionResultsWriter.cs`: the existing results-writing pattern in
  this tool; follow it rather than inventing a second output style.
- `docs/roadmap/deep-dives/p6b-unit-contract-validation.md`, §2.3: the P6b gap-reporting convention
  this output follows.

## Steps

1. Add a report writer in `src/Panopticon.Tools.ReplaySketch.Core/Services/` producing a fixed-width
   stdout table with one row per roster unit: unit, faction, action kind, measured frames, baseline
   frames, delta, verdict.
2. Render a timeout row with an explicit timeout verdict and no measured duration, never as a zero or
   a very large number that could be mistaken for a real measurement.
3. Render a gap row with the reason: unimplemented handler, unresolved definition, or
   spawn-versus-production misbehaviour.
4. Emit the same data as JSON to a configurable path so CI and future
   [A5](../../deep-dives/a5-visualization.md) tooling can consume it.
5. Print a trailing summary line with counts of passed, regressed, timed out, and gapped units.
6. Sort rows by faction then roster index so reports are easy to inspect and diff.
7. Ensure any new `.cs` file is UTF-8 with BOM. Run `dotnet format src`, then verify
   `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Services/ValidationGroundReportWriter.cs` (new)
- `src/Panopticon.Tools.TestHarness/Program.cs` (call the writer at the end of both loops)

## Acceptance criteria

- [ ] Every roster unit appears exactly once in the table.
- [ ] Timeouts and gaps are visually and structurally distinct from measured durations, in both the
      table and the JSON.
- [ ] Rows use stable faction and roster-index ordering.
- [ ] The JSON artifact contains the same data as the table and is written to a configurable path.
- [ ] The summary line reports passed, regressed, timed out, and gapped counts.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and
      `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not compare against a baseline or set an exit code here; that is `WI-P9-07`. The baseline columns
  render empty until then.
- Do not modify `ActionResultsWriter`; follow its pattern in a new writer.
- Do not add a graphical or interactive viewer.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P9-6 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
