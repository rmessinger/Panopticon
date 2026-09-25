# Task Card `WI-P8-05`: Real per-faction build trees and production assertions

| Field | Value |
|---|---|
| Card ID | `WI-P8-05` |
| Roadmap phase | [Track P · P8](../../track-p-playable-baseline.md): sub-phase P8-5, see [deep dive](../../deep-dives/p8-implementation-delta-remediation.md#3-sub-phases); remediates a gap in [P5 construction/production](../../deep-dives/p2-p5-skirmish-ai.md#6-scenario-matrix) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P5-02`, `WI-P5-05` |
| Diagrams | [P8 Architecture Diagrams](../../deep-dives/p8-implementation-delta-remediation.md#6-architecture-diagrams) |

## Goal

`WI-P5-05`'s scenario matrix fabricates a synthetic reflection-built prerequisite chain
(`NewStructureDef`/`AddPrerequisite`) rather than exercising real per-faction build-tree data; the
`faction` axis only changes a name label, so USA/GLA/China cells run the identical synthetic tree.
The matrix also asserts only `Player.CanProduceObject` toggling; never a real unit spawn or
upgrade application; and the `human` `playerType` cell bypasses `OrderProcessor`, calling
`GameLogic.CreateObject` + setting `BuildProgress` directly, which is exactly what the deep-dive
matrix assertion says not to rely on. Close all three gaps.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:138-153`: the P5 scenario matrix design: real distinct per-faction build trees, real spawn/upgrade application, `OrderProcessor` accessibility for both player types.
- `docs/roadmap/agent/cards/WI-P5-05-scenario-matrix.md`: prior card; its Result section documents exactly what was faked and why.
- `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs` (the `P5_ScenarioMatrixTests` class, roughly lines 1020-1300): the current synthetic-chain implementation to replace.
- `docs/roadmap/agent/cards/WI-P5-02-prerequisite-tech-gating.md`: notes the same synthetic-definition limitation exists in unit-level tests because `MockedGameTest` loads no real game files; read its Result section for the root cause.
- `src/Panopticon.Content/` or wherever faction INI definitions are loaded from disk in non-mocked tests (search for `[GameFact]`-based tests that do load real definitions, e.g. under `test/Panopticon.Simulation.Tests` for any existing pattern of loading real `.ini` object definitions without a full game install); determine whether any subset of real USA/GLA/China build-tree data can be loaded headlessly (e.g. from the repo's own test fixture INI files, if any exist, or from `Panopticon.Mods.Generals`'s bundled built-in rules) without requiring a full game install.
- `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs` (`BuildObject`, around lines 96-160): the real order path the `human` matrix cell should route through instead of direct `GameLogic.CreateObject`.

## Steps

1. Investigate whether real per-faction build-tree data (prerequisite chains as authored in the actual Generals/Zero Hour faction INI/build-list data) can be loaded in a headless (`MockedGameTest`) context, e.g. via `Panopticon.Mods.Generals`'s built-in rules/definitions that don't require a full game install. If real per-faction data is loadable headlessly, replace the synthetic per-cell chain with it, keeping the same tier-based axis structure.
2. If real per-faction data genuinely cannot be loaded without a full game install, keep the synthetic chain but make the `faction` axis meaningfully distinct (e.g. materially different tier depths, costs, or branching per synthetic "faction") rather than only varying a name string, and clearly document in the Result section that this remains synthetic and why.
3. Change the `human` `playerType` matrix cell to issue `Order.CreateBuildObject` through `OrderProcessor`, matching the `AI` cell's existing approach, instead of calling `GameLogic.CreateObject` + setting `BuildProgress` directly.
4. Strengthen the matrix's per-cell assertion so the gated unit/upgrade is confirmed to actually spawn or apply (a real `GameObject`/upgrade-applied check), not only that `Player.CanProduceObject` toggles true/false.
5. Update `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md`'s P5 scenario-matrix section notes (added by the P3-P6 delta-fix PR) once these gaps are closed, removing the "currently..." caveats that no longer apply.
6. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs`
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md`

## Acceptance criteria

- [ ] The `faction` axis exercises genuinely distinct build trees (real per-faction data if loadable headlessly; otherwise a documented, materially distinct synthetic tree per faction, not just a name-label change).
- [ ] The `human` `playerType` cell issues its build order through `OrderProcessor`, matching the `AI` cell.
- [ ] Each matrix cell asserts the gated unit/upgrade actually spawns/applies, not only `CanProduceObject` toggling.
- [ ] The deep-dive's P5 scenario-matrix caveats added during the delta-fix pass are updated to reflect the closed gaps (or left in place with an explanation if a gap could not be closed).
- [ ] Headless `[Fact]` tests are not skipped and do not require game files.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P5_ScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not require a full game install for these tests; they must remain CI-runnable headlessly.
- Do not silently keep asserting only `CanProduceObject`; either assert real spawn/upgrade or mark the card `Blocked` with the specific reason.
- Do not break the existing 18-cell matrix expansion or axis pruning logic; only change what each cell does internally.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the P8-5 row in [`deep-dives/p8-implementation-delta-remediation.md`](../../deep-dives/p8-implementation-delta-remediation.md#3-sub-phases).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
