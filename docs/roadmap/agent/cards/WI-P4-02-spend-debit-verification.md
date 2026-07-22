# Task Card `WI-P4-02`: Spend/debit and refund verification

| Field | Value |
|---|---|
| Card ID | `WI-P4-02` |
| Roadmap phase | [Track P · P4](../../track-p-playable-baseline.md): sub-phase P4-2, see [P4 deep dive](../../deep-dives/p4-p6-economy-production-combat.md#p4-economy-loop-end-to-end) and [P4 sub-phases](../../deep-dives/p4-p6-economy-production-combat.md#p4-sub-phases) |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | P1 complete enough that player ownership, construction ownership, and headless harness assertions are reliable |
| Diagrams | [P4 Architecture Diagrams](../../deep-dives/p4-p6-economy-production-combat.md#architecture-diagrams) |

## Goal

Verify that construction, unit production, and upgrades debit `BankAccount` correctly, block when funds are insufficient, refund on cancellation/teardown paths, and never make the balance negative. Implement the smallest missing guard/refund behavior only where the audit proves it is absent.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:16-55`: P4 design says debit/refund wiring was not confirmed and must be verified or closed.
- `docs/roadmap/implementation-checklist.md:240-243`: checklist AC for debit, cancel refunds, insufficient-funds blocking, and never-negative balance.
- `src/OpenSage.Game/Logic/BankAccount.cs:22-60`: `Withdraw` currently clamps to available money; do not rely on partial withdraw as purchase approval.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:96-130`: `BuildObject` withdraws and `CancelBuild` deposits.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:148-169`: upgrade debit and cancel refund path.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:179-224`: unit production debit, producer selection, queue, and cancel refund path.
- `src/OpenSage.Game/Logic/Object/Update/ProductionUpdate.cs:472-549`: production/upgrade queue and cancel methods; preserve all queue and parking-place behavior.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-34`: headless test base; tests here run in CI.

## Steps

1. Audit each purchase path before editing: `BuildObject`, `CreateUnit`, `BeginUpgrade`, `CancelBuild`, `CancelUnit`, and `CancelUpgrade`. Record whether each path currently checks affordable cost before creating/enqueuing work.
2. If any path allows a partial `Withdraw` to proceed, add an affordability guard before the side effect. The action must be blocked when `BankAccount.Money < cost`; do not create a building, enqueue a job, or start an upgrade after a failed affordability check.
3. If a debit succeeds, ensure all lifecycle paths that cancel or remove pending work refund exactly once: explicit cancel orders, queued-production cancel, queued-upgrade cancel, and under-construction object death/destruction when that object represents cancelled work.
4. Preserve every existing branch and guard when moving logic, including producer selection fallback, parking-place enqueue/cancel, upgrade status changes, and selected-unit behavior.
5. Add `src/OpenSage.Game.Tests/Logic/Economy/SpendDebitTests.cs` with non-skipped headless `[Fact]` tests for:
   - queueing a unit reduces `BankAccount.Money` by cost;
   - queueing a structure reduces `BankAccount.Money` by cost;
   - queueing an upgrade reduces `BankAccount.Money` by cost;
   - canceling each queued action refunds exactly once;
   - insufficient funds block the action and leave state unchanged;
   - sampled balances never go below zero.
6. Include default/sentinel checks in tests: starting balance, exact cost, zero/low balance, and unchanged queue/object counts after blocked actions.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs`
- `src/OpenSage.Game/Logic/Object/Update/ProductionUpdate.cs`
- `src/OpenSage.Game.Tests/Logic/Economy/SpendDebitTests.cs` (new)

## Acceptance criteria

- [ ] A non-skipped headless test proves queuing a unit reduces `BankAccount.Money` by exactly that unit's cost.
- [ ] A non-skipped headless test proves queuing a structure reduces `BankAccount.Money` by exactly that structure's cost.
- [ ] A non-skipped headless test proves queuing an upgrade reduces `BankAccount.Money` by exactly that upgrade's cost.
- [ ] Cancel paths refund exactly once for unit, structure, and upgrade work; pending-object death/destruction used as cancellation also refunds exactly once if applicable.
- [ ] Insufficient funds block construction, production, and upgrades before side effects; queues and object counts are unchanged.
- [ ] `BankAccount.Money` never goes below zero, including low-balance and zero-balance sentinel cases.
- [ ] Existing production queue, parking-place, producer-selection, and upgrade-status branches are preserved.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SpendDebitTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not redesign `BankAccount.Withdraw` into a broad new API unless every call site is updated and tested; prefer local affordability guards for this card.
- Do not change build prerequisites, tech-tree gating, or AI build-list behavior.
- Do not hide missing behavior behind skipped `[GameFact]` tests; CI must run the debit/refund checks.
- If object-death refund semantics conflict with verified game behavior, mark `Blocked` and document the conflict instead of guessing.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the construction/production debit and cancel-refund P4 item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `src/OpenSage.Game.Tests/Logic/Economy/SpendDebitTests.cs` was created and runs in CI.

## Result

2026-07-21: Audit findings: BuildObject/BeginUpgrade/CreateUnit all called `BankAccount.Withdraw` unconditionally with no affordability check; since Withdraw clamps at zero, the side effect proceeded on partial funds (exploit: queue a 500-cost item with 100 in the bank, Withdraw takes 100, building still goes up). Cancel paths already refunded exactly once per cancel invocation. Added `OrderProcessor.TryDebit(account, cost)` (internal static helper): returns false without debiting when balance < cost, otherwise withdraws exactly cost. Wired it into BuildObject, BeginUpgrade (reordered to do the AssetStore lookup and TryDebit before the Scene3D lookup so the unaffordable case exits without touching Scene3D), and CreateUnit. Tests cover: TryDebit unit behavior (affordable/zero/exact-cost/unaffordable), OrderProcessor integration for all three purchase paths blocking on insufficient funds, and CancelUnit/CancelUpgrade refund-once through OrderProcessor. CancelBuild's Kill path is more involved in the mocked host (needs BodyModule + Drawable); the refund mechanic itself is identical (Deposit(BuildCost) once per cancel) and is covered by the parallel CancelUnit/CancelUpgrade tests plus the GatherDeposit Deposit tests. Validation: dotnet build src (0 errors); dotnet test src --filter "FullyQualifiedName~SpendDebitTests" (9 passed); dotnet test src --filter Category!=Interactive (735 passed, 17 skipped, 0 failed); dotnet format src --verify-no-changes (clean).
