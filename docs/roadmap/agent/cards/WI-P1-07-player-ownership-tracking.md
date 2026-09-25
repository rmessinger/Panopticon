# Task Card `WI-P1-07`: Per-player object-ownership tracking

| Field | Value |
|---|---|
| Card ID | `WI-P1-07` |
| Roadmap phase | [Track P · P1: Skirmish setup & player relationships](../../track-p-playable-baseline.md) (sub-phase P1-5, see [deep dive §3.4](../../deep-dives/p1-skirmish-setup.md)) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P1-02` |
| Diagrams | [P1 Architecture Diagrams](../../deep-dives/p1-skirmish-setup.md#8-architecture-diagrams) |

## Goal

Add a maintained, O(1) per-player owned-object view: a `HashSet<GameObject>` and integer
counters for building count and unit count on `Player`, updated via
create/transfer/`OnDie`/`Destroy` hooks on `GameObject`. This is consumed by the AI (P2
target selection) and victory/defeat (P3 defeat detection) and must exist before either
can be implemented.

## Context to read (and only this)

- `src/Panopticon.Simulation/Logic/Player.cs:92-104`: existing `OwnedObjects`, `BuildingCount`,
  `UnitCount` (or equivalents) if present. Identify what already exists vs. what is missing.
  If there is nothing, you are adding from scratch.
- `src/Panopticon.Simulation/Logic/Object/GameObject.cs`: look for `Owner` property (the `Player`
  that owns this object), `OnDie` or death-notification event/method, and any
  `Destroy`/dispose lifecycle hook. Note the exact names/signatures.
- `src/Panopticon.Simulation/Logic/Object/GameObjectCollection.cs` (or equivalent): how objects
  are added to and removed from the live collection; identify the hooks where ownership
  tracking can be wired (object creation + destruction).
- `src/Panopticon.Simulation/Logic/PlayerManager.cs:50-76`: `CreatePlayers()`: the earliest point
  at which `Player` instances exist; confirm `OwnedObjects` should be initialized here or
  on the `Player` ctor.

## Steps

1. Add to `Player`:
   - `HashSet<GameObject> OwnedObjects`: the maintained owned-object set (initialize empty
     in ctor or `OnNewGame`).
   - `int BuildingCount`: count of owned objects that satisfy `IsBuilding` (or equivalent
     object-kind predicate); keep as a cached derived count (increment/decrement instead of
     scanning).
   - `int UnitCount`: count of owned units (non-building, non-neutral objects).
   Both counts are derived from `OwnedObjects`; do not maintain them independently.

2. Wire creation: wherever `GameObject` is added to the live game world (find the
   `GameObjectCollection.Add(...)` or equivalent), after the add call:
   - If `gameObject.Owner != null`, call `gameObject.Owner.OwnedObjects.Add(gameObject)`.
   - Increment the appropriate count (`BuildingCount` or `UnitCount`).

3. Wire destruction / death: wherever `GameObject.OnDie(...)` or `Destroy(...)` is called:
   - If `gameObject.Owner != null`, call `gameObject.Owner.OwnedObjects.Remove(gameObject)`.
   - Decrement the appropriate count.

4. Wire transfers (optional for this card): if `GameObject.Owner` can be reassigned (check
   for a setter), add a guard in the setter that removes from the old owner's set and adds
   to the new owner's set, maintaining counts on both sides. If no setter exists yet, skip
   this and note it as a follow-up.

5. Verify with a unit test: add a `[Fact]` to `test/Panopticon.Simulation.Tests/Logic/PlayerOwnershipTests.cs`
   (new file) that:
   - Creates a `Player` and a mock/stub `GameObject` owned by it.
   - Simulates the create hook and asserts `OwnedObjects.Count == 1`.
   - Simulates the destroy hook and asserts `OwnedObjects.Count == 0`.
   Use `MockedGameTest` as the base; no game installation required.

## Files likely to change

- `src/Panopticon.Simulation/Logic/Player.cs`: add `OwnedObjects`, `BuildingCount`, `UnitCount`.
- `src/Panopticon.Simulation/Logic/Object/GameObjectCollection.cs` (or `GameObject.cs`): create/
  destroy hooks.
- `test/Panopticon.Simulation.Tests/Logic/PlayerOwnershipTests.cs` (new test file).

## Acceptance criteria

- [ ] `Player.OwnedObjects`, `Player.BuildingCount`, `Player.UnitCount` exist and are
      non-null after `OnNewGame`.
- [ ] Creating an object owned by a player increments the set and the relevant count.
- [ ] Destroying / dying an object decrements both.
- [ ] Counts and set membership are consistent (`BuildingCount + UnitCount == OwnedObjects.Count`
      holds at all times, modulo neutral/observer objects with no owner).
- [ ] New unit test passes.
- [ ] `dotnet build src` and `dotnet test src --filter Category!=Interactive` both pass.
- [ ] `dotnet format src --verify-no-changes` passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~PlayerOwnershipTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement AI targeting or defeat detection here: P2 and P3 will consume the API
  you establish.
- Do not redesign the `GameObject` ownership model; only add tracking hooks on top of the
  existing `Owner` property.
- If `GameObject` has no lifecycle hook you can cleanly use, document the gap in Notes and
  mark `Blocked`. Do not invent a new lifecycle method without roadmap guidance.

## On completion

- Set Status to `Done` (or `Blocked` + reason).
- Tick the P1-5 item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.

## Result

Done (commit 8518ed49, "Track per-player owned objects"). `Player.OwnedObjects`
(`HashSet<GameObject>`) plus derived `BuildingCount`/`UnitCount` added to `Player.cs`;
`GameLogic.cs` calls `gameObject.Owner?.AddOwnedObject(gameObject)` on object registration and
`gameObject.Owner?.RemoveOwnedObject(gameObject)` on removal/death. Transfer-on-owner-reassignment
(step 4, optional) was completed separately by `WI-P1-11`. Covered by
`test/Panopticon.Simulation.Tests/Logic/PlayerOwnershipTests.cs`.
