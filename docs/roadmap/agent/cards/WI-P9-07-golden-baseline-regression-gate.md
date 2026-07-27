# Task Card `WI-P9-07`: Golden baseline and regression gate

| Field | Value |
|---|---|
| Card ID | `WI-P9-07` |
| Roadmap phase | [Track P · P9](../../track-p-playable-baseline.md): P9-7 Baseline and regression gate, see [deep dive §2.7](../../deep-dives/p9-unit-validation-ground.md#27-baseline-and-regression-gate) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P9-06` |
| Diagrams | [P9 Architecture Diagrams](../../deep-dives/p9-unit-validation-ground.md#6-architecture-diagrams) |

## Goal

Add `UnitTimingBaseline` with per-entry tolerance, capture and commit a golden baseline against a
real install, and make headless mode exit non-zero when a unit regresses beyond its tolerance.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p9-unit-validation-ground.md`, §2.5, §2.7, and §2.9 (determinism check).
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ValidationGroundReport.cs`: the report to compare.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ValidationGroundReportWriter.cs`: the writer from
  `WI-P9-06`, which renders the baseline columns.
- `src/OpenSage.Tools.ReplaySketch.Core/Profiles/`: where checked-in JSON data lives in this project.
- `src/OpenSage.Tools.TestHarness/TestHarnessConfig.cs`: the baseline path and refresh flag added in
  `WI-P9-05`.

## Steps

1. Add `src/OpenSage.Tools.ReplaySketch.Core/Model/UnitTimingBaseline.cs`: one entry per unit and
   action holding the golden frame count, a tolerance percentage, and a minimum absolute frame floor.
2. Implement `Compare(ValidationGroundReport)` returning per-unit verdicts. A unit passes when
   `|measured - golden| <= max(golden * tolerancePercent, minimumFrameFloor)`. A unit missing from
   the baseline is reported as new, not as a failure.
3. Implement `Load` and `Save`. `Save` runs only under the explicit refresh flag; a normal run must
   never write the baseline file.
4. Verify determinism before capturing: run headless twice and diff the JSON reports. They must be
   identical. If they are not, stop and fix the nondeterminism rather than widening tolerances.
5. Capture the baseline against a real Generals install and commit it to
   `src/OpenSage.Tools.ReplaySketch.Core/Profiles/unit-validation-ground-baseline.json`.
6. Set tolerances deliberately per entry. Do not derive them from the single capture run and do not
   use one global value.
7. In headless mode, set a non-zero process exit code when any unit regresses. A timeout is also a
   non-zero exit, reported distinctly from a regression.
8. Add `[Fact]` tests for the comparison: a value inside tolerance passes, a value outside fails, the
   minimum frame floor prevents a one-frame jitter failing a short action, and a unit absent from the
   baseline is reported as new.
9. Ensure any new `.cs` file is UTF-8 with BOM. Run `dotnet format src`, then verify
   `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Model/UnitTimingBaseline.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Profiles/unit-validation-ground-baseline.json` (new)
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/UnitTimingBaselineTests.cs` (new)
- `src/OpenSage.Tools.TestHarness/Program.cs` (exit code)

## Acceptance criteria

- [ ] Tolerance is per entry, combining a percentage with a minimum absolute frame floor.
- [ ] A one-frame jitter on a short action does not fail, and a genuine slowdown on a long action
      does.
- [ ] The baseline file is written only under the explicit refresh flag; a normal run leaves it
      untouched.
- [ ] Two consecutive headless runs produce identical JSON reports; this was verified before the
      baseline was committed.
- [ ] The committed baseline was captured against a real Generals install, and tolerances were set
      deliberately rather than copied from the capture.
- [ ] Headless exits non-zero on regression, and separately on timeout.
- [ ] A unit missing from the baseline is reported as new rather than failing the run.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and
      `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~UnitTimingBaselineTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes

# Determinism check, requires a Generals install: both reports must be identical
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet run --project src/OpenSage.Tools.TestHarness -- --headless
```

## Out of scope / guardrails

- Do not widen a tolerance to make a failing unit pass. A genuine regression is the signal this phase
  exists to produce.
- Do not let a normal run rewrite the baseline; that would turn every regression into the new normal.
- Do not fix engine-side causes of nondeterminism here; escalate them rather than absorbing them into
  the tolerance.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P9-7 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`, including any unit reported as a gap or timeout.

## Result

<!-- append after completing -->
