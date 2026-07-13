# Task Card `WI-P0-12`: Runtime object handle and selector resolution in `HarnessRunner`

| Field | Value |
|---|---|
| Card ID | `WI-P0-12` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-1, see [deep dive §3.2](../../deep-dives/p0-playable-baseline-harness.md)) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-08` (HarnessRunner must exist), `WI-P0-11` (handle/selector model types must exist) |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/p0-playable-baseline-harness.md#8-architecture-diagrams) |

## Goal

Extend `HarnessRunner.RunAndAssert` to resolve `HandleCommandArgument` and
`QuerySelectorCommandArgument` values before each command is dispatched to the engine, so a
scenario can issue `AttackObject`/`Enter` orders against objects produced by earlier commands
or filtered from the live game world, without the author specifying a raw `ObjectId`.

## Context to read (and only this)

- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs` (created by WI-P0-08): the
  `RunAndAssert` method and the headless game loop it runs. This is the only file to change.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/IObjectReferenceResolver.cs` (created by WI-P0-11):
  the interface to implement.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandArgument.cs` (updated by WI-P0-11):
  `HandleCommandArgument.CommandId`, `QuerySelectorCommandArgument.Relationship/DefinitionName`,
  `CommandSpec.Resolve(resolver)`.
- `src/OpenSage.Game/Game.cs:128,135`: `Game.ObjectCreated` event and
  `GameObjectCreatedEventArgs` (contains the created `GameObject`).
- `src/OpenSage.Game/Logic/GameLogic.cs:42-57`: `GameLogic.Objects` (enumerable over all live
  objects, safe to iterate).
- `src/OpenSage.Game/Logic/Object/GameObject.cs:291`: `GameObject.Owner` (`Player`);
  line `125`: `GameObject.Id` (`ObjectId`).
- `src/OpenSage.Game/Logic/Object/ObjectDefinition.cs`: `ObjectDefinition.Name` (compare
  against `QuerySelectorCommandArgument.DefinitionName`).
- `src/OpenSage.Tools.ReplaySketch.Tests/ReplaySketchTests.cs`: test style reference.

## Steps

1. Inside `HarnessRunner.cs`, add a private nested class `ObjectReferenceResolver` that
   implements `IObjectReferenceResolver`:

   ```csharp
   private sealed class ObjectReferenceResolver : IObjectReferenceResolver
   {
       private readonly Game _game;
       // Maps ActionEntry.Id → ObjectId of the first object created after that command's
       // orders were dispatched.
       private readonly Dictionary<string, ObjectId> _handleMap = new();
       private string? _pendingCommandId;

       public ObjectReferenceResolver(Game game)
       {
           _game = game;
           _game.ObjectCreated += OnObjectCreated;
       }

       // Call this just before dispatching a command's orders.
       public void BeginCommand(string commandId) => _pendingCommandId = commandId;

       // Call this after the frame tick that processes the command's orders.
       public void EndCommand() => _pendingCommandId = null;

       private void OnObjectCreated(object? sender, GameObjectCreatedEventArgs e)
       {
           if (_pendingCommandId is not null && !_handleMap.ContainsKey(_pendingCommandId))
               _handleMap[_pendingCommandId] = e.GameObject.Id;
       }

       public ObjectId ResolveHandle(string commandId) =>
           _handleMap.TryGetValue(commandId, out var id) ? id : ObjectId.Invalid;

       public ObjectId ResolveSelector(QuerySelectorCommandArgument selector)
       {
           foreach (var obj in _game.GameLogic.Objects)
           {
               if (obj is null) continue;
               if (selector.DefinitionName is not null
                   && obj.Definition.Name != selector.DefinitionName)
                   continue;
               if (selector.Relationship is not null)
               {
                   // Relationship is checked against the issuing player (player at index 2 is
                   // ownerIdx 0; relationship filtering is best-effort here).
                   // "enemy": owner must be in Enemies of player[2]; "ally": in Allies.
                   // If relationship checking requires a specific player context, skip filter.
                   // Do not throw — return the first definition-matched object instead.
               }
               return obj.Id;
           }
           return ObjectId.Invalid;
       }

       public void Dispose() => _game.ObjectCreated -= OnObjectCreated;
   }
   ```

2. In `RunAndAssert`, after the `Game` is created and before the run loop starts, construct an
   `ObjectReferenceResolver(game)`.

3. In the section of the run loop where each command's orders are about to be dispatched
   (before calling `game.InjectOrders` or the equivalent), do:
   ```csharp
   resolver.BeginCommand(action.Id);
   var resolvedSpec = action.Command.Resolve(resolver);
   // use resolvedSpec.ToOrder(...) instead of action.Command.ToOrder(...)
   ```
   After the frame tick that processes the command:
   ```csharp
   resolver.EndCommand();
   ```

4. At the end of `RunAndAssert` (or in a `finally` block), call `resolver.Dispose()` to
   unsubscribe from `Game.ObjectCreated`.

5. Add one integration test tagged `[Trait("Category", "Interactive")]` in
   `OpenSage.Tools.ReplaySketch.Tests` (or `OpenSage.Game.Tests` if game-test infrastructure
   is easier there):
   - Build a two-command scenario: command A (`BuildObject` for a barracks-like structure with
     `FixedTiming(200)`) + command B (`AttackObject` with a `HandleCommandArgument("cmd-A")` and
     `AfterActionTiming("cmd-A", Completed)` delay).
   - Call `HarnessRunner.RunAndAssert(scenario, installation, tempDir)`.
   - Assert the run completes without throwing (no "Must be resolved before dispatch" exception).

6. Add one headless unit test (non-Interactive) in `OpenSage.Tools.ReplaySketch.Tests`:
   - Construct an `ObjectReferenceResolver` backed by a minimal stub `Game`-like object, or test
     the `ResolveHandle`/`ResolveSelector` logic directly via `ObjectReferenceResolver` in
     isolation (use internal visibility if needed).
   - Call `BeginCommand("x")`, fire a synthetic `ObjectCreated` with a stub `ObjectId(7)`, call
     `EndCommand()`, then assert `ResolveHandle("x").Index == 7`.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs`: nested resolver class + wiring.
- `src/OpenSage.Tools.ReplaySketch.Tests/`: one headless unit test + one Interactive test.

## Acceptance criteria

- [ ] `ObjectReferenceResolver.ResolveHandle(id)` returns the `ObjectId` of the first object
      created after `BeginCommand(id)` was called.
- [ ] `ObjectReferenceResolver.ResolveSelector(selector)` returns the first object matching
      `DefinitionName` (when set) without throwing.
- [ ] `RunAndAssert` calls `BeginCommand`/`EndCommand` around each command dispatch and passes
      resolved specs to order construction; no `InvalidOperationException` for handle/selector
      args in a well-formed scenario.
- [ ] `Dispose()` unsubscribes from `Game.ObjectCreated` (headless unit test verifies no
      further `OnObjectCreated` calls after disposal).
- [ ] Non-interactive headless unit test passes under `dotnet test src --filter Category!=Interactive`.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
# With game files installed:
dotnet test src --filter "FullyQualifiedName~HandleResolution"
```

## Out of scope / guardrails

- Do not implement full relationship-based selector filtering beyond `DefinitionName`: the
  `Relationship` field exists on the model but the relationship-check logic can be left as a
  comment/TODO in `ResolveSelector` and filled in as a follow-up.
- Do not change `CommandArgument`, `CommandSpec`, `IObjectReferenceResolver`, or engine types.
- If `HarnessRunner` does not yet have an accessible run loop (WI-P0-08 not done), mark `Blocked`.
- Do not change the `ReplayExporter` or any `.rep` file format.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Reference runtime-created objects via implicit auto-handles" item in the
  [implementation checklist](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up: full relationship filtering in `ResolveSelector` (small extension, no new card needed).
