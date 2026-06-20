# Task Card: `P0-11` — Object handle and query selector model types

| Field | Value |
|---|---|
| Card ID | `P0-11` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-1, see [deep dive §3.2](../../deep-dives/playable-baseline-harness.md)) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/playable-baseline-harness.md#9-architecture-diagrams) |

## Goal

Add model types that let a scenario author reference runtime objects without knowing their
`ObjectId` at authoring time: `HandleCommandArgument` (resolves to the object produced by a
named earlier command) and `QuerySelectorCommandArgument` (resolves to an object matching a
faction/relationship/definition filter at dispatch time). Also add the `IObjectReferenceResolver`
interface and a `Resolve` extension point so the `HarnessRunner` (P0-12) can substitute
concrete `ObjectId` values before dispatch.

This is the model-only card. Runtime wiring into the run loop is P0-12.

## Context to read (and only this)

- `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandArgument.cs` — the five existing
  `CommandArgument` subtypes and their `AppendTo(Order, PositionContext, Random)` signature.
  `ObjectIdCommandArgument` (line ~52) is the type the resolver will return.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandSpec.cs:26-38` — `ToOrder(...)`:
  iterates `Arguments` calling `arg.AppendTo(...)`.
- `docs/roadmap/deep-dives/playable-baseline-harness.md §3.2` — the handle/selector design
  decision (implicit auto-handles keyed to the producing command; query selectors for
  world/enemy objects).
- `src/OpenSage.Tools.ReplaySketch.Tests/ReplaySketchTests.cs` — test style reference.

## Steps

1. In `OpenSage.Tools.ReplaySketch.Core/Model/`, create `IObjectReferenceResolver.cs`:

   ```csharp
   namespace OpenSage.Tools.ReplaySketch.Model;

   public interface IObjectReferenceResolver
   {
       /// Resolves the ObjectId of the first object produced by the command with the given id.
       /// Returns ObjectId.Invalid if not yet available.
       OpenSage.Logic.Object.ObjectId ResolveHandle(string commandId);

       /// Resolves the ObjectId of the first game object matching the selector.
       /// Returns ObjectId.Invalid if no match.
       OpenSage.Logic.Object.ObjectId ResolveSelector(QuerySelectorCommandArgument selector);
   }
   ```

2. Add a `virtual Resolve` method to the abstract `CommandArgument` base class in
   `CommandArgument.cs`. Insert it after the `AppendTo` declaration:

   ```csharp
   /// Returns a resolved version of this argument (substitutes concrete ids for handles/selectors).
   /// Default implementation returns this argument unchanged.
   public virtual CommandArgument Resolve(IObjectReferenceResolver resolver) => this;
   ```

3. In `CommandArgument.cs`, add two new concrete subtypes after `PositionCommandArgument`:

   `HandleCommandArgument` — references an object produced by an earlier command:
   ```csharp
   public sealed class HandleCommandArgument : CommandArgument
   {
       /// The Id of the ActionEntry whose produced object this argument references.
       public string CommandId { get; }

       [JsonConstructor]
       public HandleCommandArgument(string commandId) => CommandId = commandId;

       public override void AppendTo(Order order, PositionContext ctx, Random rng) =>
           throw new InvalidOperationException(
               $"HandleCommandArgument (commandId='{CommandId}') must be resolved before dispatch.");

       public override CommandArgument Resolve(IObjectReferenceResolver resolver)
       {
           var id = resolver.ResolveHandle(CommandId);
           return new ObjectIdCommandArgument(id.Index);
       }
   }
   ```

   `QuerySelectorCommandArgument` — selects an object at runtime by filter:
   ```csharp
   public sealed class QuerySelectorCommandArgument : CommandArgument
   {
       /// "enemy", "ally", "neutral", or null (any).
       public string? Relationship { get; }
       /// Definition name to match exactly, or null (any).
       public string? DefinitionName { get; }

       [JsonConstructor]
       public QuerySelectorCommandArgument(string? relationship, string? definitionName)
       {
           Relationship = relationship;
           DefinitionName = definitionName;
       }

       public override void AppendTo(Order order, PositionContext ctx, Random rng) =>
           throw new InvalidOperationException(
               "QuerySelectorCommandArgument must be resolved before dispatch.");

       public override CommandArgument Resolve(IObjectReferenceResolver resolver)
       {
           var id = resolver.ResolveSelector(this);
           return new ObjectIdCommandArgument(id.Index);
       }
   }
   ```

4. Register the two new subtypes in the `[JsonDerivedType]` list on `CommandArgument`:
   ```csharp
   [JsonDerivedType(typeof(HandleCommandArgument), "handle")]
   [JsonDerivedType(typeof(QuerySelectorCommandArgument), "query")]
   ```

5. Add a `Resolve(IObjectReferenceResolver resolver)` method to `CommandSpec`:
   ```csharp
   public CommandSpec Resolve(IObjectReferenceResolver resolver)
   {
       var resolved = Arguments.Select(a => a.Resolve(resolver)).ToList();
       return new CommandSpec(OrderType, resolved);
   }
   ```

6. Add unit tests in `OpenSage.Tools.ReplaySketch.Tests/ObjectReferenceModelTests.cs`:
   - `HandleCommandArgument_SerializesAndDeserializes`: round-trip a `HandleCommandArgument`
     through `JsonSerializer` and assert `CommandId` is preserved and the `kind` discriminator
     is `"handle"`.
   - `QuerySelectorCommandArgument_SerializesAndDeserializes`: same for `QuerySelectorCommandArgument`
     (`kind` is `"query"`).
   - `CommandSpec_Resolve_SubstitutesHandle`: build a `CommandSpec` with one
     `HandleCommandArgument("cmd-1")`. Create a stub `IObjectReferenceResolver` (inline via a
     simple class) that returns `new ObjectId(42)` for `"cmd-1"`. Call `commandSpec.Resolve(stub)`.
     Assert the resolved spec's single argument is an `ObjectIdCommandArgument` with `Value == 42`.
   - `HandleCommandArgument_AppendTo_Throws`: assert `AppendTo` throws `InvalidOperationException`.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandArgument.cs` — new subtypes + `Resolve` method.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandSpec.cs` — `Resolve(IObjectReferenceResolver)`.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/IObjectReferenceResolver.cs` (new).
- `src/OpenSage.Tools.ReplaySketch.Tests/ObjectReferenceModelTests.cs` (new).

## Acceptance criteria

- [ ] `HandleCommandArgument` and `QuerySelectorCommandArgument` round-trip through JSON
      with discriminators `"handle"` and `"query"` respectively.
- [ ] `CommandSpec.Resolve(resolver)` returns a new spec with all handle/selector args
      replaced by `ObjectIdCommandArgument`s.
- [ ] `HandleCommandArgument.AppendTo` throws `InvalidOperationException` with a message
      naming the command id.
- [ ] `QuerySelectorCommandArgument.AppendTo` throws `InvalidOperationException`.
- [ ] All four unit tests pass.
- [ ] No existing `ReplaySketchTests` broken.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src/OpenSage.Tools.ReplaySketch.Tests
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not wire this into the `HarnessRunner` run loop — that is P0-12.
- Do not implement a production `IObjectReferenceResolver` — only the interface and model
  types belong here.
- Do not change `PositionContext`, `ActionEntry`, `ReplayExporter`, or existing argument types.
- `ObjectId` lives in `OpenSage.Logic.Object`; import only what is needed. Do not change the
  engine `Order` type.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up card `P0-12` (runtime resolution in `HarnessRunner`) depends on this card.
