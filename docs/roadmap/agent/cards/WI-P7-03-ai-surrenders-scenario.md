# Task Card `WI-P7-03`: AISurrenders regression scenario

| Field | Value |
|---|---|
| Card ID | `WI-P7-03` |
| Roadmap phase | [Track P · P7](../../track-p-playable-baseline.md): P7-3 AISurrenders, see [deep dive §6](../../deep-dives/p7-end-to-end-regression.md#6-sub-phases) and [§2](../../deep-dives/p7-end-to-end-regression.md#2-the-six-end-state-scenarios) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | Track P phases `P0`, `P2`, `P3`, `P4`, `P5`, `P6`, `P6b` complete; `WI-P3-04` done for `OrderType.EndGame` surrender/end-of-match flow |
| Diagrams | [P7 Architecture Diagrams](../../deep-dives/p7-end-to-end-regression.md#8-architecture-diagrams) |

## Goal

Create the checked-in P7 AISurrenders scenario where the scripted AI player issues `OrderType.EndGame` and the run proves `Decided/Surrendered` with the human side winning.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:38-45`: AISurrenders expected outcome.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:47-54`: scripted AI progress requirement.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:58-70`: universal invariants, including AI progress.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:113-113`: P7-3 sub-phase row.
- `docs/roadmap/deep-dives/p7-end-to-end-regression.md:298-300`: `OrderType.EndGame` dependency on P3-4.
- `docs/roadmap/implementation-checklist.md:337-351`: outcome and invariant ACs.
- `src/OpenSage.Game/Logic/Orders/OrderType.cs:3-6`: `EndGame` enum value.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:426-428`: `EndGame` processing path.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/PlayerSlotConfig.cs:6-24`: AI slot configuration fields.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ActionEntry.cs:7-20`: action entries for scripted AI orders.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs:32-127`: run-and-assert entry point.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionEvaluator.cs:23-73`: invariant subscription and early abort behavior.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:169-189`: outcome assertion surface to wire to P3 `GameOutcome`.

## Steps

1. Add `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/ai-surrenders.json` as static scenario data.
2. Configure one human and one AI/scripted opponent slot. Before surrender, script at least one AI order so the AI-progress invariant has meaningful data.
3. Emit `OrderType.EndGame` from the AI player's command stream via the P0 open command model.
4. Assert `OutcomeStatus.Decided`, `OutcomeReason.Surrendered`, human side as winner, and the AI player as loser. Verify default/sentinel `GameOutcome` values do not pass.
5. Add or extend focused P7 scenario test wiring so this scenario is loaded from JSON and run through `HarnessRunner.RunAndAssert` as a real non-skipped test.
6. Attach available universal invariants, including AI progress where supported; P7-07 will audit all three invariants across all six scenarios.
7. If any new `.cs` file is created, ensure it is UTF-8 with BOM. Run `dotnet format src`, then verify `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Scenarios/Regression/ai-surrenders.json` (new)
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P7RegressionScenarioTests.cs` (new or updated focused test wiring)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/CodeAssertionRegistry.cs` (only if an AI-progress predicate must be registered for the scenario)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` (only if outcome paths still need P3 wiring)

## Acceptance criteria

- [ ] `ai-surrenders.json` exists as static checked-in scenario data.
- [ ] The AI/scripted opponent issues at least one order before surrender, then emits `OrderType.EndGame` through the command path.
- [ ] The scenario terminates with `OutcomeStatus.Decided` and `OutcomeReason.Surrendered`.
- [ ] Winner and loser assertions identify the human side and surrendering AI player respectively.
- [ ] AI progress is asserted where applicable and reports the offending frame if it fails.
- [ ] The focused test is not skipped and fails loudly on an assertion or scenario failure.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P7RegressionScenarioTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Script the `EndGame` order rather than depending on AI strategy to decide when to surrender.
- Do not implement missing P3-4 surrender logic here; block instead.
- Do not bypass ReplaySketch by setting `GameOutcome` directly in test code.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the relevant P7 checklist item(s) in [`implementation-checklist.md`](../../implementation-checklist.md) only if this card completes that item.
- Append a result line to repo-root `.exchange`.
- Confirm `ai-surrenders.json` and its focused test wiring were created.

## Result

<!-- append after completing -->
