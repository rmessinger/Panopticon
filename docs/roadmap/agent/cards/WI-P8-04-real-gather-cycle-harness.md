# Task Card `WI-P8-04`: Real gather-cycle harness verification

| Field | Value |
|---|---|
| Card ID | `WI-P8-04` |
| Roadmap phase | [Track P · P8](../../track-p-playable-baseline.md): sub-phase P8-4, see [deep dive](../../deep-dives/p8-implementation-delta-remediation.md#3-sub-phases); remediates a gap in [P4 economy loop](../../deep-dives/p4-p6-economy-production-combat.md#p4-economy-loop-end-to-end) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P4-01`, `WI-P4-04`, `WI-P4-05` |
| Diagrams | [P8 Architecture Diagrams](../../deep-dives/p8-implementation-delta-remediation.md#6-architecture-diagrams) |

## Goal

`WI-P4-01`/`WI-P4-04` explicitly scoped the harvester search→approach→gather state machine as out
of scope, testing only the dump-transition half via direct `SupplyCenterDockUpdate.DumpBoxes`/
`Deposit` calls and white-box state injection. The P4 scenario matrix (`WI-P4-05`) likewise runs a
synthetic 16-frame dump/debit loop rather than a real harvester scenario through the P0/ReplaySketch
harness, and its "at least one unit was produced" assertion only counts successful `BankAccount`
debits, not an actual spawned unit. Add the missing half: at least one headless scenario that drives
a real harvester unit through the full `SearchingForSupplySource → Approaching → Requesting →
Gathering → DumpingSupplies` cycle via `Scene3D`/pathfinding (not white-box state injection), and
strengthen the matrix's production assertion to require a real spawned unit.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:13-38`: P4 design intent: "harvester gathers → BankAccount balance rises → a queued unit/structure debits the balance".
- `src/Panopticon.Simulation/Logic/AI/AIUpdate/SupplyAIUpdate.cs`: the full gather-cycle state machine (`SearchingForSupplySource`, `Approaching`, `Requesting`, `Gathering`, `DumpingSupplies`) and `TickSupplyState()`.
- `src/Panopticon.Simulation/Logic/Object/Update/SupplyCenterDockUpdate.cs`, `SupplyWarehouseDockUpdate.cs`: dock-side dump/request handling already covered by existing tests.
- `test/Panopticon.Simulation.Tests/Logic/Economy/` (gather/deposit and supply-cycle test files from `WI-P4-01`/`WI-P4-04`): existing test structure and the white-box injection technique used to bypass the gather state machine; read to understand exactly what is NOT yet covered.
- `test/Panopticon.Simulation.Tests/MockedGameTest.cs:24-70`: headless test base and what Scene3D/PartitionCellManager support it does or does not provide (`WI-P5-01`'s Result section notes null-guards were added for headless `Scene3D`/`PartitionCellManager` creation; check whether that is sufficient for movement/pathfinding, or whether further guards are needed).
- `src/Panopticon.Tools.ReplaySketch.Core/`: the P0 harness scenario-authoring surface, if the real gather cycle is easier to drive through an authored ReplaySketch scenario than raw unit tests.
- `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:793-895` (`P4` matrix cells): the synthetic dump/debit loop and `jobsQueued` counter to replace or supplement with a real production assertion.

## Steps

1. Investigate whether `MockedGameTest`'s headless host supports enough of `Scene3D`/pathfinding/`PartitionCellManager` to move a harvester unit toward a supply source and dock. If not, identify and add the minimal additional null-guards/seams needed (following the pattern already established in `WI-P5-01`'s Result section), scoped tightly; do not build a full renderer or asset pipeline.
2. Add a new headless test (e.g. `RealGatherCycleTests.cs`) that creates a harvester unit and a supply source, ticks the game, and asserts the unit's `SupplyAIUpdate` state actually progresses through `SearchingForSupplySource → Approaching → Requesting/Gathering → DumpingSupplies` via real ticks, not manual state field assignment, and that `BankAccount` rises as a result.
3. In the P4 scenario matrix, replace or add to the "at least one unit was produced" assertion so it verifies a real `GameObject` was created via `ProductionUpdate`/`CreateUnit`, not merely a successful `TryDebit` call. If threading a real production path through the matrix's mocked host is infeasible, leave the existing debit-counter assertion in place but rename/reword it accurately and add the real-spawn assertion as a separate, additional check.
4. If real movement/pathfinding genuinely cannot be exercised headlessly within this card's effort budget, mark the card `Blocked` with a precise technical reason (e.g. "requires a real Scene3D navigation mesh which MockedGameTest cannot construct") rather than silently downgrading scope; this is a known-hard problem per the prior cards' own disclosed gaps.
5. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Logic/Economy/` (new test file)
- `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs`
- `src/Panopticon.Simulation/Logic/AI/AIUpdate/SupplyAIUpdate.cs` (only if a genuine bug blocks headless execution, not for new features)
- `test/Panopticon.Simulation.Tests/MockedGameTest.cs` (only if additional headless seams are required)

## Acceptance criteria

- [ ] At least one headless test drives a harvester unit through the real gather state machine via ticks (not white-box state field injection) and observes `BankAccount` rise as a result.
- [ ] The P4 scenario matrix's production assertion verifies (or is supplemented to verify) a real spawned `GameObject`, not only a successful debit.
- [ ] If full real-movement coverage is infeasible headlessly, the card is marked `Blocked` with a specific, named technical reason rather than silently reducing scope.
- [ ] Headless `[Fact]` tests are not skipped and do not require game files.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Economy|FullyQualifiedName~TrackPScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not build new renderer/visual features; this is a headless test-coverage card.
- Do not remove the existing dump-transition tests from `WI-P4-01`/`WI-P4-04`; add to them.
- Do not fabricate a "passing" real-movement test by secretly still injecting state; if the harness cannot genuinely drive movement, say so and stop rather than faking coverage.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the P8-4 row in [`deep-dives/p8-implementation-delta-remediation.md`](../../deep-dives/p8-implementation-delta-remediation.md#3-sub-phases).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
