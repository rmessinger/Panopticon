# Task Card `WI-P1-03`: Unit test for reciprocal player relationships

| Field | Value |
|---|---|
| Card ID | `WI-P1-03` |
| Roadmap phase | [Track P · P1: Skirmish setup & player relationships](../../track-p-playable-baseline.md) (sub-phase P1-1, see [deep dive §3.1](../../deep-dives/p1-skirmish-setup.md)) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | [`WI-P1-02`](WI-P1-02-implement-onnewgame-relationships.md) (the reciprocal wiring under test) |
| Diagrams | [P1 Architecture Diagrams](../../deep-dives/p1-skirmish-setup.md#8-architecture-diagrams) |

## Goal

Add a small xUnit test that locks in the reciprocal behaviour of `Player.AddAlly`/`AddEnemy`
delivered by [`WI-P1-02`](WI-P1-02-implement-onnewgame-relationships.md): allying/enmitying one player must
update both players' sets, and the operation must be idempotent. This guards the relationship
invariant that P2/P3/P6 rely on.

## Context to read (and only this)

- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-141`: the `MockedGameTest` base and its `Generals` /
  `ZeroHour` `TestGame` (an `IGame` with a working `AssetStore`). Use it to satisfy the `Player` ctor.
- `src/OpenSage.Game.Tests/Logic/Object/GameObjectTests.cs:1-23`: a minimal example of a
  `MockedGameTest` subclass + xUnit `[Fact]`/`Assert` to replicate the shape of.
- `src/OpenSage.Game/Logic/Player.cs:141-151`: the `Player(uint id, PlayerTemplate? template, in ColorRgb color, IGame game)`
  ctor (initializes `Allies`/`Enemies`).
- `src/OpenSage.Game/Logic/Player.cs:124-128,779-786`: `Allies`/`Enemies` sets and the
  `AddAlly`/`AddEnemy` methods under test.

## Steps

1. Create `src/OpenSage.Game.Tests/Logic/PlayerRelationshipTests.cs`, namespace
   `OpenSage.Tests.Logic`, class `PlayerRelationshipTests : MockedGameTest`.
2. Add a helper that builds a `Player` via the ctor using the base's `Generals` game, e.g.
   `new Player(id, null, new ColorRgb(0, 0, 0), Generals)`.
3. Test 1: `AddEnemy` is reciprocal: create players `a` and `b`; call `a.AddEnemy(b)`; assert
   `a.Enemies.Contains(b)` and `b.Enemies.Contains(a)`.
4. Test 2: `AddAlly` is reciprocal: same shape, assert both `Allies` sets contain the other.
5. Test 3: idempotent: call `a.AddEnemy(b)` twice; assert each `Enemies` set has exactly one
   entry (`Assert.Single`) and nothing was thrown.
6. Keep it pure-unit: no map load, no `Scene3D`, no `[GameFact]`. These run in CI (no game files).

## Files likely to change

- `src/OpenSage.Game.Tests/Logic/PlayerRelationshipTests.cs` (new file only).

## Acceptance criteria

- [ ] New test file with at least the three `[Fact]`s above, all passing.
- [ ] Tests extend `MockedGameTest` and need no installed game files (not `[GameFact]`).
- [ ] `dotnet test src --filter "FullyQualifiedName~PlayerRelationshipTests"` runs and passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~PlayerRelationshipTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not test `OnNewGame`/team-derived relationships here (it needs map players + setup): this
  card covers the `AddAlly`/`AddEnemy` primitives only. A broader `OnNewGame` test can be a later card.
- Do not modify production code; if a test can't pass because `WI-P1-02` isn't done/correct, mark
  `Blocked` and report (don't "fix" production from a test card).

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching item(s) in the [implementation checklist](../../implementation-checklist.md) (P1-1).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - An `OnNewGame` team→relationship integration test once a lightweight player-setup fixture exists.

## Result

Done (commit d5acbbb7, delivered alongside `WI-P1-02`).
`src/OpenSage.Game.Tests/Logic/PlayerRelationshipTests.cs` covers `AddEnemy`/`AddAlly` reciprocity
and idempotency; `PlayerManagerTests.cs` covers the `OnNewGame` team-derived wiring.
