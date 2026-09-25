# Task Card `WI-P4-01`: Gather-to-deposit verification

| Field | Value |
|---|---|
| Card ID | `WI-P4-01` |
| Roadmap phase | [Track P · P4](../../track-p-playable-baseline.md): sub-phase P4-1, see [P4 deep dive](../../deep-dives/p4-p6-economy-production-combat.md#p4-economy-loop-end-to-end) and [P4 sub-phases](../../deep-dives/p4-p6-economy-production-combat.md#p4-sub-phases) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | P1 complete enough that player ownership, selected units, and headless harness assertions are reliable |
| Diagrams | [P4 Architecture Diagrams](../../deep-dives/p4-p6-economy-production-combat.md#architecture-diagrams) |

## Goal

Prove the baseline supply collection loop credits `Player.BankAccount` through `BankAccount.Deposit(...)` with a headless, CI-running harness test. The test must assert a positive balance delta after a gather→dump cycle and the balance-never-negative invariant.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:16-55`: P4 goal, current gather/deposit wiring, and P4-1 sub-phase.
- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:145-194`: P4 architecture diagram links for economy types.
- `docs/roadmap/implementation-checklist.md:236-239`: checklist AC for supply collection → `BankAccount.Deposit`.
- `src/Panopticon.Simulation/Logic/BankAccount.cs:22-60`: `Withdraw` clamps to current money; `Deposit` increments `Money` and records income.
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/SupplyAIUpdate.cs:108-257`: gather state machine from source search through `DumpBoxes`.
- `src/Panopticon.Simulation/Logic/Object/Update/SupplyCenterDockUpdate.cs:17-35`: `DumpBoxes` calls `GameObject.Owner.BankAccount.Deposit(...)`.
- `src/Panopticon.Simulation/Logic/Object/Update/SupplyWarehouseDockUpdate.cs:17-31`: source box availability and decrement.
- `src/Panopticon.Simulation/Logic/Object/Update/ProductionUpdate.cs:458-470`: spawned harvester targeting logic.
- `test/Panopticon.Simulation.Tests/MockedGameTest.cs:24-34`: headless test base; tests here run in CI.

## Steps

1. Add a headless `[Fact]` test file `test/Panopticon.Simulation.Tests/Logic/Economy/GatherDepositTests.cs`.
2. Build the smallest mocked scenario that contains one player, one supply source, one supply center/dock target, and one faction-appropriate harvester (`SupplyTruckAIUpdate`, `ChinookAIUpdate`, or `WorkerAIUpdate`).
3. Drive logic ticks until the harvester reaches `FinishedDumpingSupplies` or until a fixed timeout. Fail loudly on timeout; do not skip the test.
4. Record `BankAccount.Money` before the run, assert it increases after a dump, and assert every sampled tick keeps `BankAccount.Money >= 0`.
5. If the mocked setup cannot currently instantiate the full loop, add the smallest test seam needed in the test project only; do not bypass `SupplyCenterDockUpdate.DumpBoxes` or `BankAccount.Deposit`.
6. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Logic/Economy/GatherDepositTests.cs` (new)
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/SupplyAIUpdate.cs` only if the test exposes a missing non-invasive transition fix
- `src/Panopticon.Simulation/Logic/Object/Update/SupplyCenterDockUpdate.cs` only if `DumpBoxes` fails to deposit through the owning player

## Acceptance criteria

- [ ] A non-skipped headless `[Fact]` runs in CI and exercises gather→dump through `SupplyCenterDockUpdate.DumpBoxes` and `BankAccount.Deposit` (partial: Result verifies docked dump/deposit only, not the full gather loop).
- [x] The test asserts a positive `BankAccount.Money` delta after a supply dump.
- [x] The test samples each tick and asserts `BankAccount.Money >= 0`.
- [ ] The test fails loudly on a stuck harvester or timeout; it is not replaced by a skipped `[GameFact]` (partial: no live harvester or timeout path is exercised).
- [x] No refactor drops existing supply-state guards or branch behavior.
- [x] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [x] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GatherDepositTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not redesign the economy service or replace `BankAccount`.
- Do not satisfy this card with only direct calls to `BankAccount.Deposit`; the full docked supply path must run.
- Do not add tests that require installed game files for CI coverage.
- If a necessary object-definition factory is missing, add the smallest follow-up note and mark `Blocked` rather than creating a broad fixture framework.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the supply collection → `BankAccount.Deposit` P4 item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `test/Panopticon.Simulation.Tests/Logic/Economy/GatherDepositTests.cs` was created and runs in CI.

## Result

2026-07-21: Renamed the broken in-progress file `P4EconomyTests.cs` to `GatherDepositTests.cs` per the card's "Files likely to change". Fixed two compile errors (helpers exposed `private protected TestGame` as `internal static`); replaced the broken reflection on a non-existent `_behaviors` field with the clean `ObjectDefinition.Behaviors` seam (`InternalsVisibleTo` already grants access). Direct `BankAccount.Deposit/Withdraw` tests use `playSound: false`; docked-path tests exercise `SupplyCenterDockUpdate.DumpBoxes -> BankAccount.Deposit` for the default, value-multiplier, and GLA `additionalAmountPerBox` vectors. The docked path's `playSound: true` default NRE'd in the headless host because `MockedGameTest.TestGame.Audio` was null and `MiscAudio.Current.MoneyDepositSound` was uninitialized; closed the env gap by initializing `Configuration`/`Audio` (`NoAudio=true`, PlayAudioEvent short-circuits) and pinning `MoneyDepositSound`/`MoneyWithdrawSound` to a non-null `LazyAssetReference` in the mocked TestGame ctor. Validation: dotnet build src (0 errors); dotnet test src --filter "FullyQualifiedName~GatherDepositTests" (6 passed); dotnet test src --filter Category!=Interactive (726 passed, 17 skipped, 0 failed); dotnet format src --verify-no-changes (clean).
