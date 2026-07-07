# Implementation Plan: Track P · P0-3: Gameplay `OrderType` Mapping

> Track: [Track P: Playable Baseline](../track-p-playable-baseline.md)
> Phase: [P0: ReplaySketch arbitrary-command harness](../deep-dives/playable-baseline-harness.md), sub-phase P0-3: Exporter generalization
> Checklist item: [implementation-checklist.md](../implementation-checklist.md), P0 item "Map the gameplay-relevant `OrderType` set (move, attack, construct, produce, ability) to exporter order emission"
> Depends on: [track-p-p0-3-runtime-object-references.md](track-p-p0-3-runtime-object-references.md), because attack and enter orders need object references.
>
> Audience: human overview of the implementation plan. Agent context sits below the divider.

## Problem

`CommandSpec.ToOrder` (`Model/CommandSpec.cs`) can already build an `Order` of any `OrderType` from
typed arguments, so the model is general. But there is no validated, documented mapping from the
gameplay verbs a baseline match needs (move, attack, construct, produce, ability) to the exact
`OrderType` + argument shape `OrderProcessor` expects. The legacy `BuildOrders` switch
(`ReplayExporter.cs:213-268`) hardcodes only five canned shapes, and several of them encode
non-obvious sequencing (e.g. `ClearSelection` before `CreateUnit`/`ForceAttackGround` so the order
routes to the right producer/attacker, `:240,:260`). Authors writing open `CommandSpec`s have no
catalog of "to issue verb X, emit `OrderType` Y with args [..] in this order", so they cannot
reliably produce a working order without reverse-engineering `OrderProcessor`.

Checklist item AC:
- For each of move/attack/construct/produce/ability, an authored command produces the expected
  `OrderProcessor` effect in a headless run (a state assertion passes).

## Approach

This is primarily a correctness-and-cataloging task: confirm the argument shape each verb's
`OrderType` requires by reading `OrderProcessor.Process` handlers, encode each as a tested
`CommandSpec` factory, and document the catalog.

### Ground each verb against `OrderProcessor`

For each verb, read the authoritative handler in
`src/OpenSage.Game/Logic/Orders/OrderProcessor.cs` and the matching `Order.Create*` helper in
`Logic/Orders/Order.cs`, and record the exact `OrderType` + ordered argument list + any required
selection precondition:

| Verb | Likely `OrderType` | Notes to confirm in `OrderProcessor` |
|---|---|---|
| Move | `MoveTo` | position arg; requires a prior `SetSelection` of the mover(s) |
| Attack (object) | `AttackObject` | object-id arg (uses [object references](track-p-p0-3-runtime-object-references.md)); selection precondition |
| Attack (ground) | `ForceAttackGround` | position arg; `ClearSelection`/selection handling (cf. legacy `:260`) |
| Construct | `BuildObject` | definition-hash + position + angle (cf. legacy `:220`) |
| Produce (unit) | `CreateUnit` | definition-hash + queue-place args (cf. legacy `:246-248`); producer selection |
| Produce (upgrade) | `BeginUpgrade` | producer object-id + upgrade-hash : confirm handler |
| Ability / special power | `SpecialPower*` | confirm the `SpecialPower` arg layout (power id, target pos/object, options) |

(The table is the starting hypothesis; the implementation must verify each against the real handler
and correct it : `OrderProcessor.Process` is the source of truth, per the
[P0 deep dive §2.3](../deep-dives/playable-baseline-harness.md).)

### Encode as tested factories

- Add `CommandSpec` factory methods (e.g. `Move(PositionSpec)`, `AttackObject(objectRef)`,
  `AttackGround(PositionSpec)`, `Construct(defName, PositionSpec, angle)`,
  `Produce(defName, count)`, `UseSpecialPower(...)`) that assemble the confirmed `OrderType` +
  arguments, reusing the existing `CommandArgument` kinds and the object-reference model. These give
  authors a correct, discoverable catalog instead of hand-building argument lists.
- Where a verb needs a selection precondition (move/attack/produce), encode the required
  `SetSelection`/`ClearSelection` emission as part of the factory's output (a small ordered list of
  orders), matching the legacy sequencing that already works (`:240,:260`).

### Exporter

- `BuildOrders` open-command branch (`:206-209`) already lowers a `CommandSpec` via `ToOrder`. Extend
  it so a factory that yields a multi-order sequence (selection + action) emits each in order. Keep
  legacy `ActionType` handling unchanged.

### Document the catalog

- Add a short "verb → order" table to the [P0 deep dive](../deep-dives/playable-baseline-harness.md)
  (or a sibling reference note) so future authors/agents have the mapping without re-reading
  `OrderProcessor`.

---

> Agent implementation details : the sections below list exact files, methods, and
> tests for agents executing this plan. Human readers can stop here.

## Files to change

| File | Change |
|---|---|
| `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandSpec.cs` | Add verb factory methods for move/attack/construct/produce/ability |
| `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs` | Emit multi-order factory sequences in the open-command branch |
| `docs/roadmap/deep-dives/playable-baseline-harness.md` | Add the verb→`OrderType` catalog table |
| `src/OpenSage.Game.Tests/Tools/ReplaySketch/ReplaySketchTests.cs` | One headless test per verb (effect assertion) |

## Tests

- Per-verb headless effect tests (in `OpenSage.Game.Tests`, `Category!=Interactive`): author a single
  command for each of move/attack/construct/produce/ability, run headless via the
  [entry point](track-p-p0-6-run-and-assert-entry-point.md)/harness, and assert the expected
  `OrderProcessor` effect (object moved/created/attacked) via a state assertion. If the headless
  boot lands with P0-5, gate these behind it and cross-reference.
- Model-level tests (no boot): each factory produces an `Order` of the expected `OrderType` with the
  expected argument count/types, and emits the required selection precondition order(s).

## Out of scope

- The object-reference substrate (auto-handles/selectors) → its own plan
  ([track-p-p0-3-runtime-object-references.md](track-p-p0-3-runtime-object-references.md)); this plan
  consumes it for attack/enter.
- The assertion vocabulary the effect tests use → [track-p-p0-5-assertion-layer.md](track-p-p0-5-assertion-layer.md).
- Exhaustive coverage of all ~110 `OrderType`s : only the baseline-match verb set is in scope; the
  rest remain expressible via raw `CommandSpec` but are not given factories here.
