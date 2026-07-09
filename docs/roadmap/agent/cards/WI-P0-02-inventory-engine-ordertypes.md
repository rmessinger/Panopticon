# Task Card: `WI-P0-02` — Inventory the engine's `OrderType` vocabulary

| Field | Value |
|---|---|
| Card ID | `WI-P0-02` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) |
| Status | `Done` (superseded by deep-dive audit) |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/p0-playable-baseline-harness.md#8-architecture-diagrams) |

> Superseded. The engine `OrderType`/argument inventory this card requested is captured in the
> [P0 deep dive](../../deep-dives/p0-playable-baseline-harness.md) §2.3 (the `Order`/`OrderArgument*`
> model, the ~110-value enum, and the `OrderProcessor.Process` handled subset — the authoritative
> list of what a command can actually do). Do not redo this survey.

## Goal

Produce a categorized inventory of the engine's full `OrderType` command vocabulary (~110 values)
and the argument model it uses, so P0 can target "express any engine order" with a concrete
list. Read-only; no code changes.

## Context to read (and only this)

- `src/OpenSage.Game/Logic/Orders/OrderType.cs` — the full enum (note the inline comments describing
  each order's arguments).
- `src/OpenSage.Game/Logic/Orders/Order.cs` — the `Order` object and how arguments are attached.
- `src/OpenSage.Game/Logic/Orders/OrderArgumentType.cs` and `OrderArgumentValue.cs` — the argument
  type system.

## Steps

1. Group the `OrderType` values into functional buckets (e.g. Selection, Group management, Movement,
   Combat/attack, Construction/production, Abilities/special powers, Camera/UI, Game control). Use
   the section comments already in the enum as a starting point.
2. For 6–10 representative, gameplay-relevant orders (e.g. a move, an attack, a build/construct,
   a unit-production, a special-power), record the `OrderArgumentType`s each expects, from the enum
   comments and `Order`/argument code.
3. Flag which buckets are needed for a baseline skirmish (movement, attack, construct, produce)
   vs. which are UI-only and lower priority for P0.
4. Write the inventory into Notes below as: `bucket → order values → (for the representative
   ones) argument types`.

## Files likely to change

- This card file only (append to Notes). No source changes.

## Acceptance criteria

- [ ] All `OrderType` values are assigned to a bucket (none dropped).
- [ ] Representative orders have their argument types recorded.
- [ ] Notes flag the minimal order set a baseline skirmish needs.

## Validation commands

Read-only — no build/test. Verify every `OrderType` value appears in exactly one bucket in Notes.

## Out of scope / guardrails

- Do not design the ReplaySketch model or change code — this is a survey that feeds the design.

## On completion

- Set Status to `Done`.
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - A design card (escalate to high-effort model) for the generalized command model + assertion
    layer, citing this inventory and `WI-P0-01`.

## Notes

<!-- Append findings here. -->
