# Task Card `WI-P6-05`: Attack-object approach gap

| Field | Value |
|---|---|
| Card ID | `WI-P6-05` |
| Roadmap phase | [Track P · P6](../../track-p-playable-baseline.md): sub-phase P6-1, see [deep dive §P6](../../deep-dives/p4-p6-economy-production-combat.md#p6-combat-and-command-verification) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [P6 Architecture Diagrams](../../deep-dives/p4-p6-economy-production-combat.md#architecture-diagrams) |

## Goal

Fix vehicles/units stopping at the edge of an enemy structure instead of attacking it: `OrderType.AttackObject`/`ForceAttackObject` assigns a weapon target but never calls `AIUpdate.SetTargetPoint`, so units never walk into weapon range for a normal "attack" order.

## Context to read (and only this)

- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:311-378`: `AttackObject`/`ForceAttackObject` handler (missing `SetTargetPoint`) vs. `ForceAttackGround` (correct reference implementation).
- `src/OpenSage.Game/Logic/Object/Update/AIUpdate/AIUpdate.cs:327-390`: `Update()` already contains the generic "move toward `TargetPoints[0]`, stop once `CurrentWeapon.HasValidTarget`" logic; this only needs `TargetPoints` to be populated to work correctly; no `AttackState` changes are required.
- `src/OpenSage.Game/Logic/AI/AIStates/AttackState.cs`: confirmed this AI-state stub (only `Persist()` overridden, no `Update()`/`OnEnter()`) is unrelated to the fix; `AIUpdate.Update()`'s target-point movement runs independently of the state machine.

## Steps

1. In `OrderProcessor.cs`'s `AttackObject`/`ForceAttackObject` case, resolve the target `GameObject` from `objectId` and call `unit.AIUpdate?.SetTargetPoint(targetObject.Translation)` alongside the existing `unit.CurrentWeapon?.SetTarget(...)`, mirroring `ForceAttackGround`.
2. Guard for a missing/destroyed target object (skip `SetTargetPoint` if not found, but still attempt `SetTarget` as before).
3. Do not touch `AttackState.cs` or the AI state machine; the fix relies solely on `AIUpdate.Update()`'s existing target-point/weapon-range logic.

## Files likely to change

- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs`

## Acceptance criteria

- [x] `AttackObject`/`ForceAttackObject` orders cause the unit to move into weapon range of the target object, not just assign a weapon target.
- [x] Units still stop once in range (no behavior change to `ForceAttackGround` or existing stop-on-valid-target logic).
- [x] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement `AttackState`'s chase/approach/aim/fire sub-states; not needed for this fix.
- Do not change `ForceAttackGround`'s existing behavior.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - Optional future cleanup: implement `AttackState`'s stub sub-states so state-machine-driven callers (if any are added later) also get chase/approach/aim/fire behavior, since they currently no-op.

## Result

Fixed: `AttackObject`/`ForceAttackObject` now resolve the target `GameObject` and call
`unit.AIUpdate?.SetTargetPoint(targetObject.Translation)` before assigning the weapon target,
reusing the exact movement/stop-in-range mechanism already proven correct by `ForceAttackGround`
(`AIUpdate.Update()`'s `TargetPoints` walk + `CurrentWeapon.HasValidTarget` stop check). No
`AttackState` changes were needed since that logic runs independent of the AI state machine.

Validation: `dotnet build src` (clean), `dotnet test src --filter Category!=Interactive` (712
passed), `dotnet format src --verify-no-changes` (clean). Also re-ran the full headless
`alpine-assault-usa-vs-gla-full-build` TestHarness scenario as a regression check; it still
completes with `EnemyBaseDestroyed` (that scenario exercises `ForceAttackGround`, not
`AttackObject`/`ForceAttackObject`, so it validates no regression rather than the new path
directly).

True black-box verification of `AttackObject`/`ForceAttackObject` specifically (a live scenario
issuing that exact order against a resolved enemy object id) is currently blocked by the same
harness gap as `WI-P0-12` (runtime object-id reference resolution isn't wired into the
JSON scenario/action model yet); building that out here would exceed this card's `S` scope.
Recommend a follow-up card once `WI-P0-12` lands.
