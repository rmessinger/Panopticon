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
- B: Polymorphic `ActionEntry` — `[JsonPolymorphic]` with `LegacyActionEntry` (non-nullable `Type: ActionType`) and `CommandActionEntry` (non-nullable `Command: CommandSpec`). Requires a JSON discriminator.
- C (chosen): `CommandSpec` is always present; `ActionEntry.Type` is a stored `ActionType?` field (null for open commands). The five legacy verbs are static `CommandSpec` factory methods; `ActionEntry` tracks which legacy type it represents as a plain nullable enum field. JSON: `"type": "Construct"` for legacy actions, `"command": {...}` for open commands — no discriminator needed.

Rationale: Option C produces the cleanest model. `CommandSpec` holds only `OrderType + Arguments` with no string tags. Legacy identity lives on `ActionEntry` (as `ActionType?`), not inside the command model. No backward-compat shims needed since ReplaySketch JSON is internal with no versioning requirement.

## Approach

Introduce a `CommandSpec` model that maps directly onto an engine `Order`. The five legacy
`ActionType` values are preserved as a convenience enum on `ActionEntry` only (not in `CommandSpec`).
`ReplayExporter.BuildOrders` dispatches on `action.Type` for legacy actions and calls
`action.Command.ToOrder(...)` for open commands.

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
  - No legacy identity stored here. Static factory methods (`Construct()`, `GatherResources()`, etc.) produce the matching `CommandSpec` for legacy-type construction.

- `ActionEntry` changes:
  - `Type: ActionType?` — stored field (null for open commands, non-null for legacy actions).
  - `Command: CommandSpec` — always set. Legacy constructor calls `CommandSpec.ForLegacyType(type)`.

### ReplayExporter changes
  - `BuildOrders`: branches on `action.Type == null` (open command → `CommandSpec.ToOrder(...)`) vs non-null (legacy action → existing switch on `ActionType` enum).
  - `Export`: deferred action creation (`DeferredAction`) guards for open-command triggers; they are skipped with a comment (auto-handle for open-command triggers is a later P0 item).

### Tests
New `CommandSpecTests` class in `ReplaySketchTests.cs`:
- Integer test: `CommandSpec` with an `IntegerCommandArgument(42)` → `ToOrder` produces an Integer argument of value 42.
- Float test: `CommandSpec` with a `FloatCommandArgument` → `ToOrder` produces a Float argument.
- Boolean test: `CommandSpec` with a `BooleanCommandArgument` → `ToOrder` produces a Boolean argument.
- ObjectId test: `CommandSpec` with `ObjectIdCommandArgument(5)` → `ToOrder` produces an ObjectId argument wrapping `ObjectId(5)`.
- Position test: `CommandSpec` with `PositionCommandArgument(NormalizedPosition(0.5, 0.5))` → `ToOrder` resolves to a world-space Position argument (test constructs a stub `PositionContext`).
- Legacy round-trip test: `ReplayScenario.CreateAlpineAssaultUSAvGLA()` still loads; all `ActionEntry`s carry their original `ActionType` value.
- JSON round-trip test: An `ActionEntry` with an open `CommandSpec` serializes and deserializes correctly; `Type` is null.

## Files to change

| File | Change |
|---|---|
| `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandArgument.cs` | NEW — polymorphic argument hierarchy |
| `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandSpec.cs` | NEW — open command model + `ToOrder`; no legacy identity stored |
| `src/OpenSage.Tools.ReplaySketch.Core/Model/ActionEntry.cs` | Add `CommandSpec Command` field; make `Type: ActionType?` a stored field (not derived); update JSON converter |
| `src/OpenSage.Tools.ReplaySketch.Core/Model/ActionType.cs` | Kept — still used as `ActionEntry.Type` for legacy dispatch |
| `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs` | `BuildOrders`: dispatch on `action.Type` enum; open-command branch calls `ToOrder` |
| `src/OpenSage.Tools.ReplaySketch.Core/Profiles/ActionTreeController.cs` | Verify compiles cleanly after `ActionEntry` changes |
| `src/OpenSage.Game.Tests/Tools/ReplaySketch/ReplaySketchTests.cs` | Add `CommandSpecTests` class |

## Out of scope (later P0 items)

- Object-id auto-handles (implicit handle keyed to producing command) → checklist item 4
- Query selectors for world/enemy objects → checklist item 4
- Removal of `KnownDefinitions` / faction-agnostic resolution → agent-guide card `P0-03`
- `DeferredAction` trigger redesign for `CommandSpec`-based triggers → later item
- Validation/assertion layer → checklist item 6
- Headless run-and-assert entry point → checklist item 7
