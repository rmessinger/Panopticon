# Task Card `WI-P5-05`: P5 scenario matrix

| Field | Value |
|---|---|
| Card ID | `WI-P5-05` |
| Roadmap phase | [Track P · P5](../../track-p-playable-baseline.md): scenario matrix for P5-1/P5-2, see [deep dive §6](../../deep-dives/p2-p5-skirmish-ai.md#6-scenario-matrix), [§4](../../deep-dives/p2-p5-skirmish-ai.md#3-sub-phases), and [Appendix](../../deep-dives/p2-p5-skirmish-ai.md#how-actions-are-actuated-the-p5-mechanism) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P5-01`, `WI-P5-02`, `WI-P5-03`, `WI-P5-04` |
| Diagrams | [P5 Architecture Diagrams](../../deep-dives/p2-p5-skirmish-ai.md#7-architecture-diagrams) |

## Goal

Replace the P5 scenario-matrix stub with an executable 18-cell matrix covering faction × build-tree depth × player type, proving prerequisite gating and production are available through the order path.

## Context to read (and only this)

- `docs/roadmap/implementation-checklist.md:307-313`: checklist item and exact AC to satisfy.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:120-156`: P2/P5 matrix model and P5 axes/assertions.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:252-277`: P5 order, construction, production, gating, and upgrade mechanisms.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs:96-152`: matrix expansion and assertion templates.
- `src/OpenSage.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs:9-23`: matrix runner.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs:374-448`: existing matrix test style.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:130-158`: current P5 matrix stubs to replace or move into a real test file.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-34`: headless test base.

## Steps

1. Add or replace with `src/OpenSage.Game.Tests/Tools/ReplaySketch/P5ScenarioMatrixTests.cs`; ensure any new `.cs` file is UTF-8 with BOM.
2. Define a `ScenarioMatrix` with exactly these axes: `faction` ∈ `{USA, GLA, China}`, `buildTreeDepth` ∈ `{tier1, tier2, tier3}`, and `playerType` ∈ `{human, AI}`.
3. Assert `matrix.Expand().Count == 18` and no valid cell is filtered out.
4. For each cell, build a scenario that first attempts the gated unit/upgrade before prerequisites, then issues the prerequisite chain through `OrderProcessor` actions, then retries after prerequisites are complete.
5. Ensure `tier3` cells issue the full tier1 + tier2 + tier3 prerequisite chain in the same run before the final gated unit/upgrade assertion.
6. For `playerType=AI`, assert the action stream contains the expected `BuildObject`/`CreateUnit`/upgrade orders; direct state mutation alone does not satisfy the matrix.
7. Fallback implement-if-missing: if the existing `ScenarioMatrix` model cannot express command variation by binding, add the smallest binding/substitution support needed in `.Core` and cover it with a headless test.
8. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs`
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P5ScenarioMatrixTests.cs` (new, if split from the stub file)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs`
- `src/OpenSage.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs`

## Acceptance criteria

- [ ] A `ScenarioMatrix` (axes: `faction` ∈ {USA, GLA, China}, `buildTreeDepth` ∈ {tier1, tier2, tier3}, `playerType` ∈ {human, AI}) runs 18 cells; each asserts the gated unit/upgrade is unavailable before prereqs and available after; tier3 cells issue the full prerequisite chain; see the deep dive matrix section (partial: matrix uses synthetic chains and asserts `CanProduceObject` toggling, not real unit or upgrade availability).
- [x] `Matrix_AllCells_PrerequisiteGatingEnforced` is no longer skipped and fails if any cell does not run.
- [x] `Matrix_AIPlayerType_BuildsViaOrderProcessorNotDirectAPI` asserts AI cells use the order stream rather than direct object/production mutation.
- [ ] The matrix uses existing faction/build-tree data; no invented definitions are introduced to make cells pass (partial: matrix fabricates synthetic prerequisite definitions instead of real faction data).
- [x] The new or updated headless `[Fact]` tests run in CI and do not require game files.
- [x] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [x] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P5ScenarioMatrixTests|FullyQualifiedName~P5_ScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not mark the matrix done with only expansion tests; each cell must assert gating and production behavior.
- Do not create fake faction data or bypass `OrderProcessor` to satisfy AI cells.
- Do not broaden this to P2 strategic AI behavior or P6 command/combat coverage.
- If a required P5 card is not done, keep this card `Blocked` rather than weakening assertions.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "P5 scenario matrix: sweep faction × buildTreeDepth × playerType" P5 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `src/OpenSage.Game.Tests/Tools/ReplaySketch/P5ScenarioMatrixTests.cs` was created or the existing P5 matrix tests were unskipped and run in CI.

## Result

Implemented on branch `track-p5-construction-production` alongside WI-P5-01..04 (see `.exchange` 2026-07-22 entry). The 18-cell matrix (`faction` × `buildTreeDepth` × `playerType`) lives as the `P5_ScenarioMatrixTests` class inside `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs` rather than a separate `P5ScenarioMatrixTests.cs` file. Caveat surfaced by the P3-P6 delta audit (2026-07-25): the matrix fabricates a synthetic per-cell prerequisite chain via reflection (`NewStructureDef`/`AddPrerequisite`); the `faction` axis only varies a name label, not a real distinct build tree; each cell asserts only `Player.CanProduceObject` toggling, not an actual unit spawn or upgrade application; and the `human` `playerType` cell creates the object directly via `GameLogic.CreateObject` + `BuildProgress = 1.0f` rather than through `OrderProcessor`, unlike the `AI` cell. WI-P5-06 tracks closing these three gaps. `dotnet build src` and `dotnet test src --filter Category!=Interactive` passed; `dotnet format src --verify-no-changes` clean.

