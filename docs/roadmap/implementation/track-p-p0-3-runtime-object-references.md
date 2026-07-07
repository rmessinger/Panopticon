# Implementation Plan: Track P · P0-3: Runtime Object References (Auto-handles and Selectors)

> Track: [Track P: Playable Baseline](../track-p-playable-baseline.md)
> Phase: [P0: ReplaySketch arbitrary-command harness](../deep-dives/playable-baseline-harness.md), part of sub-phase P0-3: the object-reference model exporter generalization needs
> Checklist item: [implementation-checklist.md](../implementation-checklist.md), P0 item "Reference runtime-created objects via implicit auto-handles and world/enemy objects via query selectors"
> Decision: [open-questions.md](../open-questions.md), "P0 harness: runtime object references" (implicit auto-handles, opt-in names, query selectors).
>
> Audience: human overview of the implementation plan. Agent context sits below the divider.

## Problem

`ObjectIdCommandArgument` (`Model/CommandArgument.cs`) can only carry a literal `uint` object id.
But the objects a command needs to target usually do not exist at authoring time:

- A unit/structure produced by an earlier command (e.g. attack "the barracks built in command #3",
  garrison "the Humvee produced in command #5"). Its `ObjectId` is assigned at runtime by
  `OrderProcessor` when the build/production completes : unknowable when the `.rep` is authored.
- A world or enemy object that the script did not create (e.g. attack the enemy Command Center). It
  has a runtime id the author cannot predict and should not hardcode.

The exporter writes the `.rep` ahead of the run, so any order needing such an id must be resolved at
dispatch, not at export. Today only legacy `AttackEnemyBase` sidesteps this by attacking ground
(a position, not an object), and `GatherResources` hardcodes a placeholder `ObjectId(2)`
(`ReplayExporter.cs:228`). There is no general mechanism.

Checklist item AC:
- A scenario issues `AttackObject`/`Enter` against an object produced by an earlier command without
  the author writing an id, and another command targets an enemy structure via a selector; both
  resolve in a headless run (a state assertion passes).

## Design (per the settled Decision)

Two reference kinds, both resolved at dispatch against `GameLogic`, replacing the unresolved
`ObjectId` argument at injection time:

1. Produced-object handles (implicit). An object-id argument may reference the command that
   produced the target rather than a literal id. The author normally writes nothing : the reference
   is keyed to the producing command, with an opt-in explicit name for readability. At dispatch, the
   handle resolves to the `ObjectId` that the producing command's build/production yielded. This
   generalizes today's `AfterActionTiming` trigger model (`Model/TimingConfig.cs`,
   `Services/DeferredAction.cs`), which already keys one action to another by id and already runs the
   dependent order at runtime via the deferred-injection path.

2. Query selectors (world/enemy objects). For objects the script did not create, a small selector
   vocabulary : by `faction` / `relationship` (enemy/ally/neutral) / `definition` name / `proximity`
   to a position : resolved against `GameLogic`'s object collection at dispatch (the same kind of
   query `Player.GetEnergy` does over all objects, `Player.cs:92`). Returns one `ObjectId`
   (first/nearest match, deterministic tie-break).

### Model additions (Core)

- A new `CommandArgument` subtype (or a polymorphic `ObjectRef` carried by `ObjectIdCommandArgument`)
  representing one of: `Literal(uint)` (today's behavior), `ProducedBy(commandId, opt-in name)`, or
  `Selector(SelectorSpec)`. JSON discriminated like the existing `CommandArgument` hierarchy
  (`[JsonPolymorphic] kind`).
- `SelectorSpec` : `{ relationship?, faction?, definitionName?, near? (PositionSpec), pick = first|nearest }`.
- Literal ids resolve at export (as now); `ProducedBy` and `Selector` ids cannot : they mark the
  command as deferred and resolve at dispatch.

### Resolution at dispatch (runtime)

- Extend the deferred-injection path used by the harness
  (`TestHarness/Program.cs` `BuildDeferredInjector`/`DrainPendingInjections`, and
  `Services/DeferredAction.cs`) so that, before `Game.InjectOrders`, any unresolved object-id
  argument is filled in:
  - `ProducedBy` → look up the `ObjectId` recorded when the producing command completed. The
    `ReplayActionMonitor` already observes production/build completion
    (`Services/ReplayActionMonitor.cs`); capture the produced `ObjectId` there and key it by the
    producing command id.
  - `Selector` → run the query against `game` (`GameLogic` object collection + `Player`
    relationships) at injection time.
- This keeps authored ordering and reactive triggers intact; it only adds id resolution.

### Exporter changes

- `BuildOrders` (`ReplayExporter.cs:200`): a command containing a `ProducedBy`/`Selector` argument is
  routed to the deferred path (not written to the `.rep` body) with enough metadata for runtime
  resolution, mirroring how `AfterActionTiming` actions are deferred today (`:67-86`). Literal-only
  commands keep exporting inline.

---

> Agent implementation details : the sections below list exact files, methods, and
> tests for agents executing this plan. Human readers can stop here.

## Files to change

| File | Change |
|---|---|
| `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandArgument.cs` | Add produced-by + selector object-reference variants (or an `ObjectRef` on the id argument) |
| `src/OpenSage.Tools.ReplaySketch.Core/Model/SelectorSpec.cs` | NEW : query selector model |
| `src/OpenSage.Tools.ReplaySketch.Core/Services/DeferredAction.cs` | Carry unresolved object refs + the producing-command key |
| `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayActionMonitor.cs` | Record produced `ObjectId` per completed producing command |
| `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs` | Route ref-bearing commands to the deferred path; keep literal-only inline |
| `src/OpenSage.Tools.TestHarness/Program.cs` | Resolve `ProducedBy`/`Selector` ids before `InjectOrders` (in the drain step) |
| `src/OpenSage.Game.Tests/Tools/ReplaySketch/ReplaySketchTests.cs` | Tests below |

## Tests

Model/export-level (no game boot, in `OpenSage.Game.Tests` or `ReplaySketch.Tests`):
- A command with a `ProducedBy` ref serializes/deserializes and is routed to the deferred path
  (not written inline) by the exporter.
- A `Selector` JSON round-trips and parses its relationship/faction/definition/near fields.

Headless (in `OpenSage.Game.Tests`, `Category!=Interactive`), if a mocked/headless boot is available:
- Author "build barracks (cmd A)" then "produce unit at A's structure (cmd B)" with B referencing A
  by produced-by handle; run headless; assert the unit was created at the expected producer.
- Author a selector targeting an enemy structure; assert resolution returns that structure's id.
  (If headless boot for these is not yet wired, this assertion lands with the
  [run-and-assert entry point](track-p-p0-6-run-and-assert-entry-point.md); cross-reference it.)

## Out of scope (later P0 items)

- The full gameplay `OrderType` emission table (move/attack/produce/ability) →
  [track-p-p0-4-ordertype-mapping.md](track-p-p0-4-ordertype-mapping.md). This plan provides the
  object-reference substrate those orders consume.
- The assertion layer and entry point → P0-4/P0-5 plans.
- Generalizing `AfterActionTiming` triggers to fire on open-command (non-legacy) producers is part of
  this work where needed for produced-by handles, but broader trigger-model redesign is deferred.
