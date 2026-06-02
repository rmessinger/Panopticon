# Implementation Plan: Track P · P0-1 — Replace `ActionType` with Open `CommandSpec` Model

> Track: [Track P — Playable Baseline](../track-p-playable-baseline.md)
> Phase: [P0 — ReplaySketch arbitrary-command harness](../deep-dives/playable-baseline-harness.md) · sub-phase P0-1: Open command model
> Checklist item: [implementation-checklist.md](../implementation-checklist.md) — first item under P0

## Problem

The first item in the roadmap implementation checklist (P0-1) is to replace the closed
`ActionType` enum with an open command model (`CommandSpec`) that can express any engine
`OrderType`. Today, ReplaySketch can only author five canned action shapes
(`Construct`, `GatherResources`, `RecruitBasicUnit`, `AttackEnemyBase`, `EnemyBaseDestroyed`).
The roadmap's "playable baseline" track needs to drive arbitrary game commands.

Checklist item AC:
- A scenario can specify a command as `OrderType` + typed args → exports to the matching engine `Order`
- A unit test covers ≥1 command per `OrderArgumentType`
- The five legacy `ActionType`s still round-trip (existing `alpine-assault-usa-vs-gla.json` loads & exports unchanged)

## Design Decision

How to carry either a legacy `ActionType` or a new `CommandSpec` in `ActionEntry` without making both fields nullable. Options considered:

- A: `ActionType.Command` sentinel — `Type` stays non-nullable; add `ActionType.Command` as a new enum value. `Command: CommandSpec?` is only non-null when `Type == Command`. JSON: new actions write `"type": "Command"` + `"command": {...}`.
- B: Polymorphic `ActionEntry` — `[JsonPolymorphic]` with `LegacyActionEntry` (non-nullable `Type: ActionType`) and `CommandActionEntry` (non-nullable `Command: CommandSpec`). Requires a JSON discriminator; needs backward-compat handling for existing profiles without a `"kind"` field.
- C (chosen): Replace `ActionType` entirely — Remove `ActionType` enum from `ActionEntry`; `CommandSpec` is always present; the five legacy verbs become static `CommandSpec` factory methods. Legacy JSON loading maps `"type": "Construct"` → the matching factory method.

Rationale: Option C produces the cleanest model — `ActionEntry` has no nullable or sentinel fields, and the legacy verbs are first-class `CommandSpec` instances rather than a parallel type system. The JSON migration path (legacy `"type"` string → factory method) is straightforward and keeps existing profiles working without a discriminator field.

## Approach

Introduce a `CommandSpec` model that maps directly onto an engine `Order`. Keep the five legacy
`ActionType` values as JSON sugar. Extend `ActionEntry` to carry either the legacy type or the
new open command. The `ReplayExporter.BuildOrders` method emits orders for both paths.

### New model types

- `CommandArgument` — polymorphic hierarchy mirroring `OrderArgumentType`:
  - `IntegerCommandArgument(int Value)` → kind: `integer`
  - `FloatCommandArgument(float Value)` → kind: `float`
  - `BooleanCommandArgument(bool Value)` → kind: `boolean`
  - `ObjectIdCommandArgument(uint Value)` → kind: `objectId` (maps to `ObjectId(Value)`)
  - `PositionCommandArgument(PositionSpec Spec)` → kind: `position` (reuses existing `PositionSpec` so positions can be landmark-relative)
  - Each implements `OrderArgument Resolve(PositionContext ctx, Random rng)`.

- `CommandSpec` — the open command model:
  - `OrderType OrderType` (the full engine enum)
  - `List<CommandArgument> Arguments`
  - `Order ToOrder(int playerIndex, PositionContext ctx, Random rng)` — creates an `Order` and appends all resolved arguments. Public method → directly unit-testable without going through `Export`.

### ReplayExporter changes
  - `BuildOrders`: add a branch for `CommandSpec`-based actions that calls `CommandSpec.ToOrder(...)`.
  - The existing `switch (action.Type)` (or equivalent) remains for legacy actions.
  - `Export`: deferred action creation (`DeferredAction`) guards for `CommandSpec`-based triggers; they are skipped with a comment (auto-handle for `CommandSpec` triggers is a later P0 item).

### Tests
New `CommandSpecTests` class in `ReplaySketchTests.cs`:
- Integer test: `CommandSpec` with an `IntegerCommandArgument(42)` → `ToOrder` produces an Integer argument of value 42.
- Float test: `CommandSpec` with a `FloatCommandArgument` → `ToOrder` produces a Float argument.
- Boolean test: `CommandSpec` with a `BooleanCommandArgument` → `ToOrder` produces a Boolean argument.
- ObjectId test: `CommandSpec` with `ObjectIdCommandArgument(5)` → `ToOrder` produces an ObjectId argument wrapping `ObjectId(5)`.
- Position test: `CommandSpec` with `PositionCommandArgument(NormalizedPosition(0.5, 0.5))` → `ToOrder` resolves to a world-space Position argument (test constructs a stub `PositionContext`).
- Legacy round-trip test: `ReplayScenario.CreateAlpineAssaultUSAvGLA()` still loads; all `ActionEntry`s carry their original legacy type, not a `CommandSpec`.
- JSON round-trip test: An `ActionEntry` with a `CommandSpec` serializes and deserializes correctly.

## Files to change

| File | Change |
|---|---|
| `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandArgument.cs` | NEW — polymorphic argument hierarchy |
| `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandSpec.cs` | NEW — open command model + `ToOrder` |
| `src/OpenSage.Tools.ReplaySketch.Core/Model/ActionEntry.cs` | Replace `ActionType` field with `CommandSpec`; add five static factory methods for legacy verbs; add legacy JSON deserializer |
| `src/OpenSage.Tools.ReplaySketch.Core/Model/ActionType.cs` | DELETE — replaced by `CommandSpec` factory methods |
| `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs` | `BuildOrders`: add CommandSpec branch; guard deferred trigger |
| `src/OpenSage.Tools.ReplaySketch.Core/Profiles/ActionTreeController.cs` | Verify compiles cleanly after `ActionEntry` changes |
| `src/OpenSage.Game.Tests/Tools/ReplaySketch/ReplaySketchTests.cs` | Add `CommandSpecTests` class |

## Out of scope (later P0 items)

- Object-id auto-handles (implicit handle keyed to producing command) → checklist item 4
- Query selectors for world/enemy objects → checklist item 4
- Removal of `KnownDefinitions` / faction-agnostic resolution → agent-guide card `P0-03`
- `DeferredAction` trigger redesign for `CommandSpec`-based triggers → later item
- Validation/assertion layer → checklist item 6
- Headless run-and-assert entry point → checklist item 7
