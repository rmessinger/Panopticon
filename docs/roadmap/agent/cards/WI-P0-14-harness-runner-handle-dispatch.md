# Task Card `WI-P0-14`: Harness runner direct-dispatch path for handle-dependent commands

| Field | Value |
|---|---|
| Card ID | `WI-P0-14` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (supplemental to P0-1, see [deep dive §3.2](../../deep-dives/p0-playable-baseline-harness.md)) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-08` (HarnessRunner must exist), `WI-P0-11` (handle/selector model types must exist) |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/p0-playable-baseline-harness.md#8-architecture-diagrams) |

## Goal

Unblock WI-P0-12 by giving `HarnessRunner` two new capabilities:

1. A `HandleTracker` class that maps `CommandEntry.Id → ObjectId` via `BeginCommand` /
   `EndCommand` / `OnObjectCreated` (testable headlessly, no SDL2 dependency).
2. A modified `RunAndAssert` that:
   - Partitions actions into concrete (exported to `.rep`) and handle-dependent (dispatched
     directly in the run loop via `game.InjectOrders`).
   - Passively tracks what each concrete command creates (via `BeginCommand` / `EndCommand`
     around the frame update that fires the `.rep`-dispatched order).
   - Resolves and directly dispatches handle-dependent commands at their scheduled frame.

After this card, `RunAndAssert` can execute a scenario that references objects produced by
earlier commands, without the caller specifying raw `ObjectId` values.

## Context to read

- `src/Panopticon.Tools.ReplaySketch.Core/HarnessRunner.cs`: the file to rewrite.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/CommandArgument.cs`: `HandleCommandArgument`,
  `QuerySelectorCommandArgument`, `CommandSpec.Resolve(resolver)`.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/IObjectReferenceResolver.cs`: interface to implement.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/ReplayExporter.cs`: export path used for
  concrete actions; handle-dependent actions must be replaced with no-ops (`EnemyBaseDestroyed`)
  to preserve cumulative frame alignment.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/TerrainValidator.cs`: `BuildContextPublic`
  (internal; accessible from `HarnessRunner` in the same namespace).
- `src/Panopticon.Simulation/Game.cs`: `Game.ObjectCreated` event; a new public helper
  `FindFirstObjectByDefinitionName(string)` is added to `Game` for `ResolveSelector`.

## Steps

1. Add `HandleTracker` class in `Services/` (no Game dependency):
   - `BeginCommand(id)`: push id onto a stack.
   - `EndCommand()`: pop the stack.
   - `OnObjectCreated(ObjectId)`. If the stack is non-empty, store
     `top → id` (first create per command; subsequent creates are ignored).
   - `ResolveHandle(id) → ObjectId`: returns the stored id or `ObjectId.Invalid`.

2. Add public `FindFirstObjectByDefinitionName(string) → ObjectId` to `Game.cs`:
   - Iterates `GameLogic.Objects` and returns the first object whose `Definition.Name`
     matches; returns `ObjectId.Invalid` if none found.

3. Add private nested `ObjectReferenceResolver : IObjectReferenceResolver, IDisposable`
   inside `HarnessRunner`:
   - Wraps `HandleTracker`; subscribes to `game.ObjectCreated` in constructor;
     unsubscribes in `Dispose`.
   - Delegates `BeginCommand`/`EndCommand` to tracker.
   - `ResolveHandle` → tracker.
   - `ResolveSelector` → `game.FindFirstObjectByDefinitionName(selector.DefinitionName)`.

4. Add `ContainsUnresolvableArgs(ActionEntry) → bool` helper: true if any argument in
   `action.Command.Arguments` is `HandleCommandArgument` or `QuerySelectorCommandArgument`.

5. Add `BuildConcreteScenario(ReplayScenario) → ReplayScenario`: creates a scenario copy
   where handle-dependent actions are replaced with `EnemyBaseDestroyed` no-ops (same timing,
   same id) so that subsequent concrete actions land at the correct cumulative frame.

6. Add `BuildActionSchedule(ReplayScenario, MapMetadataService) → List<ScheduledAction>`:
   iterates every player's non-`AfterActionTiming` actions, accumulates cumulative frames with
   a seeded `Random(0)`, and records `(Frame, PlayerIndex, CommandId, DirectDispatch, Ctx)`.
   `DirectDispatch` is `null` for concrete actions and the `ActionEntry` itself for
   handle-dependent actions.

7. Rewrite the `RunAndAssert` run loop:
   - Export `concreteScenario` (not the original).
   - Before each `game.Update()`, call `BeginCommand` for all actions scheduled at the current
     logic frame; for handle-dependent ones, also resolve and inject orders.
   - After `game.Update()`, call `EndCommand` for each.
   - Dispose the resolver in a `finally` block.

8. Add headless unit tests in `HandleTrackerTests.cs`:
   - Basic flow: `BeginCommand` → `OnObjectCreated` → `EndCommand` → `ResolveHandle` returns id.
   - No tracking without `BeginCommand` → returns `ObjectId.Invalid`.
   - Stack semantics: two nested commands each get their first created object.

## Files to change

- `src/Panopticon.Tools.ReplaySketch.Core/Services/HandleTracker.cs`: new file.
- `src/Panopticon.Simulation/Game.cs`: add `FindFirstObjectByDefinitionName`.
- `src/Panopticon.Tools.ReplaySketch.Core/HarnessRunner.cs`: rewrite.
- `test/Panopticon.Tools.ReplaySketch.Tests/HandleTrackerTests.cs`: new headless tests.

## Acceptance criteria

- [ ] `HandleTracker` headless tests pass under `dotnet test src --filter Category!=Interactive`.
- [ ] `ContainsUnresolvableArgs` returns true only for actions whose `Command.Arguments`
      contain `HandleCommandArgument` or `QuerySelectorCommandArgument`.
- [ ] `BuildConcreteScenario` produces a scenario where no action has unresolvable args.
- [ ] `BuildActionSchedule` assigns correct cumulative frames for a known fixed-timing sequence.
- [ ] `RunAndAssert` on a concrete-only scenario produces the same report as before (no regression).
- [ ] Standard build passes: `dotnet build src` and `dotnet format src --verify-no-changes`.

## Out of scope

- RandomTiming + handle-dependent actions: frame alignment is best-effort when RandomTiming
  precedes a handle-dependent action (Export uses unseeded RNG; schedule uses `Random(0)`).
  Document in a code comment.
- Multiple handle-dependent actions at the same frame: the stack-based `HandleTracker`
  handles them in LIFO order; the last `BeginCommand`'d action gets objects first.
- Full relationship filtering in `ResolveSelector` (only `DefinitionName` is checked).

## On completion

- Set Status above to `Done`.
- WI-P0-12 is now unblocked: change its Status to `Ready`.
- Append a result line to repo-root `.exchange`.
