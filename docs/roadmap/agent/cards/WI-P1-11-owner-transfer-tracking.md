# Task Card `WI-P1-11`: Maintain owned-object counts on `GameObject.Owner` reassignment

| Field | Value |
|---|---|
| Card ID | `WI-P1-11` |
| Roadmap phase | [Track P · P1: Skirmish setup & player relationships](../../track-p-playable-baseline.md) (sub-phase P1-5, [deep dive §3.4](../../deep-dives/p1-skirmish-setup.md)) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P1-07` |
| Diagrams | [P1 Architecture Diagrams](../../deep-dives/p1-skirmish-setup.md#8-architecture-diagrams) |

## Goal

Close the ownership-transfer gap left open by `WI-P1-07`. That card wired `OwnedObjects` /
`BuildingCount` / `UnitCount` for object creation (`GameLogic.AddObject`) and destruction
(`GameLogic.DestroyObject`), but `GameObject.Owner` is a plain `internal set` auto-property, so
reassigning an object's owner at runtime does not move it between the two players' owned-object
sets or fix their counts. The captured/transferred object stays counted on its old owner and is
never counted on its new one: which silently corrupts the P2 target-selection and P3 defeat-detection
data those counts feed.

## Context to read (and only this)

- `src/OpenSage.Game/Logic/Object/GameObject.cs:291`: `public Player Owner { get; internal set; }`
  (the auto-property to convert into a guarded setter).
- `src/OpenSage.Game/Logic/Object/GameObject.cs:1636-1640`: `Owner = Team.Template.Owner;`: an
  existing runtime owner reassignment (object joins a team), proving transfers happen post-creation.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:55,476`: creation path
  (`gameEngine.GameLogic.CreateObject(...)`, `Owner = owner ?? GetCivilianPlayer()`); the initial
  assignment must not double-count, since `GameLogic.AddObject` already calls
  `Owner.AddOwnedObject(this)` after creation.
- `src/OpenSage.Game/Logic/GameLogic.cs:118-122,188-192`: the create/destroy hooks added by
  `WI-P1-07` (`gameObject.Owner?.AddOwnedObject(gameObject)` / `RemoveOwnedObject(gameObject)`).
- `src/OpenSage.Game/Logic/Player.cs:790-840`: `AddOwnedObject` / `RemoveOwnedObject` (idempotent
  via `HashSet` add/remove return value).

## Steps

1. Convert `GameObject.Owner` into an explicit property backed by a private field
   (`private Player _owner;`), keeping the `internal set` accessibility.
2. In the setter, when the value actually changes and the object is already registered:
   - If the object is currently in `_owner.OwnedObjects` (i.e. it was tracked), call
     `_owner.RemoveOwnedObject(this)` on the old owner.
   - Assign the new owner.
   - If the object is "live" (added to the world / not destroyed), call
     `newOwner.AddOwnedObject(this)`.
   The `AddOwnedObject`/`RemoveOwnedObject` methods are already idempotent (`HashSet` return-value
   guarded), so rely on that to avoid double counting rather than tracking extra state.
3. Avoid double-counting at creation. Confirm the order: the ctor sets `Owner` before
   `GameLogic.AddObject` runs its `AddOwnedObject` hook. Ensure the setter does not also add at
   construction time when the object is not yet in the world: gate the setter's `AddOwnedObject`
   call on the object already being tracked/live, or leave first-assignment to the existing
   `GameLogic.AddObject` hook. Verify by reading the creation path (`CreateObject` →
   ctor → `AddObject`) so creation still results in exactly one count, not two.
4. Update the `WI-P1-07` follow-up note: transfers are now wired.

## Files likely to change

- `src/OpenSage.Game/Logic/Object/GameObject.cs`: `Owner` setter.
- `src/OpenSage.Game.Tests/Logic/PlayerOwnershipTests.cs`: add a transfer test (below).

## Acceptance criteria

- [ ] Reassigning `gameObject.Owner` from player A to player B moves the object out of
      `A.OwnedObjects` and into `B.OwnedObjects`, and decrements A's / increments B's relevant count.
- [ ] Object creation still results in exactly one `AddOwnedObject` (no double count): verified
      by an existing create test still asserting `OwnedObjects.Count == 1`.
- [ ] Destroying a transferred object decrements the current owner's count, not the original's.
- [ ] New `[Fact]` in `PlayerOwnershipTests` covers A→B transfer counts (use `MockedGameTest`; no
      game install).
- [ ] Standard validation passes (`dotnet build src`, `dotnet test src --filter Category!=Interactive`,
      `dotnet format src --verify-no-changes`).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~PlayerOwnershipTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change the `OwnedObjects`/count API surface from `WI-P1-07`: only wire the setter.
- Do not alter the create/destroy hooks in `GameLogic` unless needed to prevent double counting;
  if you must, keep the net behaviour "exactly one add per live object, one remove on death".
- Watch the `RuntimeHelpers.GetUninitializedObject` test pattern in `PlayerOwnershipTests`: a
  setter that dereferences `_owner` must tolerate a null/old owner on first assignment.

## On completion

- Set Status to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.

## Result

Converted `GameObject.Owner` from an auto-property to a backed property. On reassignment, if the
object was already tracked in the old owner's `OwnedObjects` (checked via `HashSet.Contains`), the
setter calls `oldOwner.RemoveOwnedObject(this)` then `newOwner?.AddOwnedObject(this)`: moving the
object and fixing `BuildingCount`/`UnitCount` on both sides. First assignment (construction, before
`GameLogic.AddObject` runs) leaves `oldOwner == null`, so `wasTracked` is false and no add happens,
avoiding double-counting; `GameLogic.AddObject`'s existing explicit `AddOwnedObject` call remains
the sole initial registration.

Added three tests to `PlayerOwnershipTests`: `OwnerReassignment_MovesObjectBetweenPlayersAndFixesCounts`
(A→B transfer moves the object and counts, and destroying afterward decrements only B),
`OwnerAssignment_AtConstructionDoesNotDoubleCount` (construction-style assignment doesn't add until
`GameLogic.AddObject` does), plus retained the original `AddOwnedObject_TracksOwnedObjectsAndCounts`.

Validation: `dotnet build src` clean, `dotnet test src --filter "FullyQualifiedName~PlayerOwnershipTests"`
3 passed, `dotnet test src --filter Category!=Interactive` 670 passed / 22 skipped / 0 failed,
`dotnet format src --verify-no-changes` reports 0 CHARSET/style errors.
