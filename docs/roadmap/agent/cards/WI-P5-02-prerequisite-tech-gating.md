# Task Card `WI-P5-02`: Prerequisite and tech gating

| Field | Value |
|---|---|
| Card ID | `WI-P5-02` |
| Roadmap phase | [Track P · P5](../../track-p-playable-baseline.md): sub-phase P5-1, see [deep dive §4](../../deep-dives/p2-p5-skirmish-ai.md#3-sub-phases), [§1.4](../../deep-dives/p2-p5-skirmish-ai.md#14-p5-construction-and-production-completeness), and [Appendix](../../deep-dives/p2-p5-skirmish-ai.md#how-actions-are-actuated-the-p5-mechanism) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | P1 and P4 complete |
| Diagrams | [P5 Architecture Diagrams](../../deep-dives/p2-p5-skirmish-ai.md#7-architecture-diagrams) |

## Goal

Verify and close faction prerequisite/tech gating so `Player.CanProduceObject(...)` rejects unmet object/science chains and accepts them after the required faction build-tree prerequisites exist.

## Context to read (and only this)

- `docs/roadmap/implementation-checklist.md:261-263`: checklist item and exact AC to satisfy.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:60-69`: P5 gating scope.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:84-93`: P5-1 funding/gating row.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:268-271`: `Player.CanProduceObject` and `GameObject` gating entry points.
- `src/OpenSage.Game/Logic/Player.cs:363-427`: object/science prerequisite evaluation; preserve AND-of-OR semantics.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:1164-1192`: `CanPurchase`/`CanConstructUnit` money + owner gating.
- `src/OpenSage.Game/Logic/Object/Update/ProductionUpdate.cs:271-291`: producer-local gating.
- `src/OpenSage.Game/Logic/AI/AIData.cs:99-100` and `src/OpenSage.Game/Logic/AI/AIData.cs:242-245`: parsed `SkirmishBuildLists`.
- `src/OpenSage.Game/Logic/Map/BuildListInfo.cs:65-82` and `src/OpenSage.Game/Logic/Map/BuildListInfo.cs:228-236`: build-list fields and `IsBuildable`.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-34`: headless test base.

## Steps

1. Add `src/OpenSage.Game.Tests/Logic/Production/PrerequisiteTechGatingTests.cs` as a headless `[Fact]` test file; ensure the new `.cs` file is UTF-8 with BOM.
2. Verification path first: choose one real faction prereq chain from loaded object definitions. Assert the gated unit/structure returns `false` from `Player.CanProduceObject(...)` before prerequisites exist.
3. Construct or inject the prerequisite structures through the same completed-building state `CanProduceObject` scans (`Owner == player`, matching definition, `BuildProgress >= 1.0`), then assert the gated unit/structure returns `true`.
4. Include at least one science/upgrade-gated check if the selected chain exposes `Prerequisites.Sciences`; otherwise assert and document that the chosen chain exercises object prerequisites only.
5. Assert `GameObject.CanConstructUnit(...)` reflects both prerequisite state and money, including a not-enough-money default/sentinel case.
6. Fallback implement-if-missing: if `Player.CanProduceObject(...)` misses a parsed prerequisite shape, fix only the prerequisite evaluation while preserving the AND-of-OR branch structure and all existing null/default behavior.
7. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/Player.cs`
- `src/OpenSage.Game/Logic/Object/GameObject.cs`
- `src/OpenSage.Game.Tests/Logic/Production/PrerequisiteTechGatingTests.cs` (new)

## Acceptance criteria

- [ ] A unit/structure with unmet prerequisites is non-buildable and becomes buildable once the prereqs exist; test asserts gating for ≥1 prereq chain.
- [ ] The test verifies the default false path for unmet prerequisites and the true path after completed prerequisite structures exist.
- [ ] Money gating through `GameObject.CanConstructUnit(...)` is asserted separately from prerequisite gating.
- [ ] Existing AND-of-OR object/science prerequisite semantics are preserved without dropping any branch or guard.
- [ ] The new headless `[Fact]` runs in CI and does not require game files.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~PrerequisiteTechGatingTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not invent new faction build-tree data; use existing object definitions, prerequisites, and parsed build-list data.
- Do not implement strategic AI build-order planning here.
- Do not satisfy this card with only a skipped `[GameFact]`; gating failures must fail loudly in CI.
- If no real prereq chain is available headlessly, mark `Blocked` with the exact missing data-loading seam.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Faction build trees with prerequisites / tech gating (`Player.CanProduceObject:357-420`)" P5 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `src/OpenSage.Game.Tests/Logic/Production/PrerequisiteTechGatingTests.cs` was created and runs in CI.

## Result

<!-- append after completing -->

