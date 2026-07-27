# Task Card `WI-P9-04`: Per-unit action timer

| Field | Value |
|---|---|
| Card ID | `WI-P9-04` |
| Roadmap phase | [Track P · P9](../../track-p-playable-baseline.md): P9-4 Action timer, see [deep dive §2.5](../../deep-dives/p9-unit-validation-ground.md#25-measuring-duration) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P9-01`, `WI-P9-03` |
| Diagrams | [P9 Architecture Diagrams](../../deep-dives/p9-unit-validation-ground.md#6-architecture-diagrams) |

## Goal

Add `UnitActionTimer` measuring each unit's dispatch-to-completion duration in logic frames, with a
per-action completion signal and a per-unit frame budget that produces a timeout result distinct from
a regression.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p9-unit-validation-ground.md`, §2.4 (completion-signal column), §2.5, and
  the "Existing timing patterns to generalize" appendix entry.
- `src/OpenSage.Tools.TestHarness/EngineActionRecorder.cs:477-478`: `CurrentFrame()`, the logic-frame
  derivation to reuse.
- `src/OpenSage.Tools.TestHarness/EngineActionRecorder.cs:55-60`: the `_pendingMoves`,
  `_pendingBuilds`, and `_lastHealth` completion trackers.
- `src/OpenSage.Tools.TestHarness/EngineActionRecorder.cs:92-128`: `PollPendingMoves`, the
  arrival-detection pattern.
- `src/OpenSage.Tools.TestHarness/EngineActionRecorder.cs:133-175`: `PollHealthChanges`, the
  health-delta detection pattern.
- `src/OpenSage.Tools.TestHarness/EngineActionRecorder.cs:185-245`: `PollSupplyCycles` and
  `ApproachLeg`, including the `travelFrames = CurrentFrame() - leg.StartFrame` computation at `:240`.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/PrimaryAction.cs`: action kinds and `FrameBudget` from
  `WI-P9-01`.

## Steps

1. Add `src/OpenSage.Tools.ReplaySketch.Core/Services/UnitActionTimer.cs`.
2. Expose `OnDispatched(unitId, PrimaryAction, uint frame)` recording the start frame per unit.
3. Expose `Poll(uint frame, ...)` taking the per-frame observations it needs through an injected
   read-only seam rather than a direct `IGame` reference, so the timer stays in `.Core` and remains
   headlessly testable.
4. Implement one completion predicate per `PrimaryActionKind`: move completes when the unit's target
   points empty, attack when the assigned target's health first decreases, build when construction
   completes, gather when a full supply state cycle returns to its start state, special power when
   the ability's observable effect appears.
5. Record `frames = completionFrame - startFrame` on the first completion signal, mirroring the
   `travelFrames` computation in `EngineActionRecorder`.
6. When `frame - startFrame` exceeds the action's `FrameBudget`, record a timeout result and stop
   polling that unit. A timeout must be representable distinctly from both a completion and a
   baseline regression.
7. Add `BuildReport()` returning a `ValidationGroundReport` with timings, gaps, and timeouts as
   separate collections.
8. Add `[Fact]` tests driving the timer through a fake observation seam: a unit that completes on a
   known frame reports exactly that frame delta, and a unit that never completes reports a timeout at
   its budget and no duration.
9. Ensure any new `.cs` file is UTF-8 with BOM. Run `dotnet format src`, then verify
   `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Services/UnitActionTimer.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ValidationGroundReport.cs` (new)
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/UnitActionTimerTests.cs` (new)

## Acceptance criteria

- [ ] Durations are logic frames derived from `MapTime`/`MsPerLogicFrame`; no `DateTime`,
      `Stopwatch`, or other wall-clock source appears in the timer.
- [ ] A unit completing on a known frame reports exactly `completionFrame - startFrame`.
- [ ] A unit that never completes reports a timeout at its `FrameBudget` and contributes no duration.
- [ ] Timeouts, gaps, and durations are three distinct result collections on the report.
- [ ] `UnitActionTimer` has no direct `IGame` or `Scene3D` reference and is exercised by tests
      without a game install.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and
      `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~UnitActionTimerTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not modify `EngineActionRecorder`; reuse its patterns, do not refactor it.
- Do not compare against a baseline here; that is `WI-P9-07`.
- Do not print a report here; that is `WI-P9-06`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P9-4 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
