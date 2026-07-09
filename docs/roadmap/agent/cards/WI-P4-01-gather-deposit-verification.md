# Task Card: `WI-P4-01` — Gather-to-deposit verification

| Field | Value |
|---|---|
| Card ID | `WI-P4-01` |
| Roadmap phase | [Track P · P4](../../track-p-playable-baseline.md) — sub-phase P4-1, see [P4 deep dive](../../deep-dives/p4-p6-economy-production-combat.md#p4-economy-loop-end-to-end) and [P4 sub-phases](../../deep-dives/p4-p6-economy-production-combat.md#p4-sub-phases) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | P1 complete enough that player ownership, selected units, and headless harness assertions are reliable |
| Diagrams | [P4 Architecture Diagrams](../../deep-dives/p4-p6-economy-production-combat.md#architecture-diagrams) |

## Goal

Prove the baseline supply collection loop credits `Player.BankAccount` through `BankAccount.Deposit(...)` with a headless, CI-running harness test. The test must assert a positive balance delta after a gather→dump cycle and the balance-never-negative invariant.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:16-55` — P4 goal, current gather/deposit wiring, and P4-1 sub-phase.
- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:145-194` — P4 architecture diagram links for economy types.
- `docs/roadmap/implementation-checklist.md:236-239` — checklist AC for supply collection → `BankAccount.Deposit`.
- `src/OpenSage.Game/Logic/BankAccount.cs:22-60` — `Withdraw` clamps to current money; `Deposit` increments `Money` and records income.
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/SupplyAIUpdate.cs:108-257` — gather state machine from source search through `DumpBoxes`.
- `src/OpenSage.Game/Logic/Object/Update/SupplyCenterDockUpdate.cs:17-35` — `DumpBoxes` calls `GameObject.Owner.BankAccount.Deposit(...)`.
- `src/OpenSage.Game/Logic/Object/Update/SupplyWarehouseDockUpdate.cs:17-31` — source box availability and decrement.
- `src/OpenSage.Game/Logic/Object/Update/ProductionUpdate.cs:458-470` — spawned harvester targeting logic.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-34` — headless test base; tests here run in CI.

## Steps

1. Add a headless `[Fact]` test file `src/OpenSage.Game.Tests/Logic/Economy/GatherDepositTests.cs`.
2. Build the smallest mocked scenario that contains one player, one supply source, one supply center/dock target, and one faction-appropriate harvester (`SupplyTruckAIUpdate`, `ChinookAIUpdate`, or `WorkerAIUpdate`).
3. Drive logic ticks until the harvester reaches `FinishedDumpingSupplies` or until a fixed timeout. Fail loudly on timeout; do not skip the test.
4. Record `BankAccount.Money` before the run, assert it increases after a dump, and assert every sampled tick keeps `BankAccount.Money >= 0`.
5. If the mocked setup cannot currently instantiate the full loop, add the smallest test seam needed in the test project only; do not bypass `SupplyCenterDockUpdate.DumpBoxes` or `BankAccount.Deposit`.
6. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game.Tests/Logic/Economy/GatherDepositTests.cs` (new)
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/SupplyAIUpdate.cs` only if the test exposes a missing non-invasive transition fix
- `src/OpenSage.Game/Logic/Object/Update/SupplyCenterDockUpdate.cs` only if `DumpBoxes` fails to deposit through the owning player

## Acceptance criteria

- [ ] A non-skipped headless `[Fact]` runs in CI and exercises gather→dump through `SupplyCenterDockUpdate.DumpBoxes` and `BankAccount.Deposit`.
- [ ] The test asserts a positive `BankAccount.Money` delta after a supply dump.
- [ ] The test samples each tick and asserts `BankAccount.Money >= 0`.
- [ ] The test fails loudly on a stuck harvester or timeout; it is not replaced by a skipped `[GameFact]`.
- [ ] No refactor drops existing supply-state guards or branch behavior.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

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
- Confirm `src/OpenSage.Game.Tests/Logic/Economy/GatherDepositTests.cs` was created and runs in CI.

## Result

<!-- append after completing -->
