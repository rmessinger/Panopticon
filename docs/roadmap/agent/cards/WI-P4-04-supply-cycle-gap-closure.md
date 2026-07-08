# Task Card: `WI-P4-04` — Supply, worker, and dozer cycle gap closure

| Field | Value |
|---|---|
| Card ID | `WI-P4-04` |
| Roadmap phase | [Track P · P4](../../track-p-playable-baseline.md) — sub-phase P4-4, see [P4 deep dive](../../deep-dives/economy-production-combat.md#p4-economy-loop-end-to-end) and [P4 sub-phases](../../deep-dives/economy-production-combat.md#p4-sub-phases) |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | P1 complete enough that player ownership, construction ownership, and headless object ticking are reliable |
| Diagrams | [P4 Architecture Diagrams](../../deep-dives/economy-production-combat.md#architecture-diagrams) |

## Goal

Close small gaps that leave supply trucks, workers, or dozers stuck in economy/build cycles. The deliverable is a CI-running sampled scenario test proving each actor completes its cycle without stalling, plus minimal fixes for any confirmed stuck transition.

## Context to read (and only this)

- `docs/roadmap/deep-dives/economy-production-combat.md:16-55` — P4 goal includes closing supply-truck/worker/dozer cycles.
- `docs/roadmap/implementation-checklist.md:248-250` — checklist AC: worker/dozer/supply-truck complete full cycles without stuck states.
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/SupplyAIUpdate.cs:15-28` — supply gather state enum.
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/SupplyAIUpdate.cs:108-257` — state transitions for gather, carry, target search, dump, and finish.
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/SupplyTruckAIUpdate.cs:6-33` — supply-truck wrapper and persistence.
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/ChinookAIUpdate.cs:9-52` — USA harvester subclass; preserve persistence branches.
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/WorkerAIUpdate.cs:34-78` and `src/OpenSage.Game/Logic/Object/Update/AIUpdate/WorkerAIUpdate.cs:183-204` — worker reset/default-state behavior.
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/DozerAIUpdate.cs:41-72` — dozer build-target wiring and update.
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/DozerAndWorkerState.cs:36-73` — shared builder state update and completion clearing.
- `src/OpenSage.Game/Logic/AI/SupplyAIUpdateStateMachine.cs:16-47` — incomplete supply state-machine configuration.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-34` — headless test base; tests here run in CI.

## Steps

1. Add `src/OpenSage.Game.Tests/Logic/Economy/SupplyCycleGapTests.cs` with sampled, non-skipped headless `[Fact]` tests.
2. Test the supply-truck path: source search → approach → request/gather boxes → carry → target search → dump → `FinishedDumpingSupplies` → resume/search again. Fail if the state does not change for a bounded number of ticks while prerequisites are available.
3. Test the worker path in both roles:
   - supply role completes gather→dump without stalling;
   - builder role receives a build target, advances construction, and clears `BuildTarget`/`ActivelyConstructing` after completion.
4. Test the dozer path: receives a build target, reaches active construction, advances `BuildProgress`, and clears build state after completion.
5. If a cycle stalls, fix only the confirmed transition or guard. Preserve every existing branch, persistence field, skipped-byte count, and default state when touching AI update/state-machine code.
6. Add stuck-state diagnostics to test failure messages: actor type, state name, frame, previous state, and sampled balance/build progress.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/SupplyAIUpdate.cs`
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/SupplyTruckAIUpdate.cs`
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/ChinookAIUpdate.cs`
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/WorkerAIUpdate.cs`
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/DozerAIUpdate.cs`
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/DozerAndWorkerState.cs`
- `src/OpenSage.Game/Logic/AI/SupplyAIUpdateStateMachine.cs`
- `src/OpenSage.Game.Tests/Logic/Economy/SupplyCycleGapTests.cs` (new)

## Acceptance criteria

- [ ] A non-skipped headless `[Fact]` proves a supply truck completes gather→dump without a stuck state.
- [ ] A non-skipped headless `[Fact]` proves a worker completes gather→dump without a stuck state.
- [ ] A non-skipped headless `[Fact]` proves worker builder mode completes construction and clears builder state.
- [ ] A non-skipped headless `[Fact]` proves a dozer completes construction and clears builder state.
- [ ] Stuck-state assertions fail loudly with actor type, state, and frame diagnostics.
- [ ] Any refactor preserves existing state-machine branches, persistence order, guards, and default/sentinel values.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SupplyCycleGapTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not rewrite the full AI state-machine architecture.
- Do not change faction build trees, map data, or game asset requirements.
- Do not replace stuck-state tests with skipped interactive coverage.
- If a stuck state requires a broader design decision, mark `Blocked` and append the exact state/frame evidence.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the supply-truck / worker / dozer cycle gaps P4 item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `src/OpenSage.Game.Tests/Logic/Economy/SupplyCycleGapTests.cs` was created and runs in CI.

## Result

<!-- append after completing -->
