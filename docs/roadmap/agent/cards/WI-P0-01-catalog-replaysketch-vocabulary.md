# Task Card: `WI-P0-01` — Catalog the current ReplaySketch command vocabulary

| Field | Value |
|---|---|
| Card ID | `WI-P0-01` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) |
| Status | `Done` (superseded by deep-dive audit) |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/playable-baseline-harness.md#9-architecture-diagrams) |

> Superseded. The investigation this card requested has been completed and captured (file:line
> grounded) in the [P0 deep dive](../../deep-dives/playable-baseline-harness.md) §2.1–2.2 (closed
> `ActionType` vocabulary, `KnownDefinitions`, and the exporter translation table). Do not redo
> this survey. The implementation follow-up is card
> [`WI-P0-03`](WI-P0-03-faction-agnostic-definition-resolution.md).

## Goal

Produce a precise, written inventory of how ReplaySketch represents commands today — the closed
action vocabulary and the hardcoded object definitions — so the P0 generalization work has an exact
"before" picture to work against. Read-only; no code changes.

## Context to read (and only this)

- `src/OpenSage.Tools.ReplaySketch.Core/Model/ActionType.cs` — the closed action enum.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ActionEntry.cs` — the per-action data model.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ReplayScenario.cs` — the scenario shape.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs` — how actions become engine
  `Order`s and a `.rep`; note the `KnownDefinitions` class (hardcoded USA/GLA defs).

## Steps

1. List every value of `ActionType` and, for each, which `ActionEntry` fields it uses
   (e.g. `BuildingDefinitionName`, `UnitDefinitionName`, `Position`, `Timing`).
2. List the hardcoded object definitions in `KnownDefinitions` and note where/how they are looked up
   (the `AssetHash.GetHash` pattern).
3. In `ReplayExporter`, identify exactly where each `ActionType` is translated into an engine
   `Order` (method + the `OrderType`/arguments used).
4. Write the findings into the card's Notes section below (append, don't overwrite), as a short
   table: `ActionType → fields used → resulting Order(s)`. Note any action that is deferred
   (runtime-injected) rather than written to the `.rep`.

## Files likely to change

- This card file only (append to Notes). No source changes.

## Acceptance criteria

- [ ] Every `ActionType` value is listed with its fields and resulting engine order(s).
- [ ] The hardcoded definition set and lookup mechanism are documented.
- [ ] Notes clearly state the limitation (closed vocabulary + fixed defs) that P0 must remove.

## Validation commands

Read-only — no build/test. Verify the Notes table covers every `ActionType` value.

## Out of scope / guardrails

- Do not change any code or design the new model — that's a later card. This is purely a survey.

## On completion

- Set Status to `Done`.
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - `WI-P0-02` (inventory engine `OrderType`s) if not already present.
  - A design card for the generalized order model (escalate: needs a high-effort model / deep dive).

## Notes

<!-- Append findings here. -->
