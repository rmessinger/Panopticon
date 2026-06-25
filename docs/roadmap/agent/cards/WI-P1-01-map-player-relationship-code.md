# Task Card: `WI-P1-01` — Map the player-relationship gap

| Field | Value |
|---|---|
| Card ID | `WI-P1-01` |
| Roadmap phase | [Track P · P1 — Skirmish setup & player relationships](../../track-p-playable-baseline.md) |
| Status | `Done` (superseded by deep-dive audit) |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [P1 Architecture Diagrams](../../deep-dives/skirmish-setup.md#8-architecture-diagrams) |

> Superseded. The relationship-gap survey this card requested is captured in the
> [P1 deep dive](../../deep-dives/skirmish-setup.md) §2.3 (the `OnNewGame()` TODO, the existing
> `Player`/`PlayerManager` relationship API, and that `AddAlly`/`AddEnemy` are non-reciprocal) plus
> the §3.1 implementation brief. Do not redo this survey. The implementation follow-up is card
> [`WI-P1-02`](WI-P1-02-implement-onnewgame-relationships.md).

## Goal

Pin down exactly what "set up player relationships" must do, by documenting the current `OnNewGame`
TODO and the relationship/team APIs already on `Player`/`PlayerManager`. Read-only; produces the
brief that a later implementation card will follow.

## Context to read (and only this)

- `src/OpenSage.Game/Logic/PlayerManager.cs` — `OnNewGame()` (the `// TODO: Setup player
  relationships.` at ~line 45) and how players are created/iterated.
- `src/OpenSage.Game/Logic/Player.cs` — any existing relationship/ally/enemy/team fields or methods
  (search for `Relationship`, `Ally`, `Enemy`, `Team`).
- `src/OpenSage.Game/Network/SkirmishManager.cs` — how skirmish player settings (teams/slots) are
  built and passed into game start.

## Steps

1. Quote the `OnNewGame()` TODO and summarize what the method currently does and does not do for
   player relationships.
2. List the relationship-related API that already exists on `Player`/`PlayerManager` (types, enums,
   methods) — what's available to call vs. what's missing.
3. Identify where skirmish slot/team data originates (`SkirmishManager`) and how it would reach
   `OnNewGame` (the data path), without changing anything.
4. Write a short implementation brief into Notes: the minimal set of relationships a
   1-human-vs-1-AI skirmish needs (self=allied, opponent=enemy, neutrals), and the exact method(s)
   that would need to set them. Mark any genuine design ambiguity for escalation.

## Files likely to change

- This card file only (append to Notes). No source changes.

## Acceptance criteria

- [ ] The current `OnNewGame` relationship gap is documented with the quoted TODO.
- [ ] Existing relationship API on `Player`/`PlayerManager` is inventoried (present vs. missing).
- [ ] Notes contain a minimal, concrete implementation brief for a 1v1 skirmish.

## Validation commands

Read-only — no build/test. Verify the brief names concrete types/methods, not vague intentions.

## Out of scope / guardrails

- Do not implement relationships yet — that's a follow-up implementation card. Survey + brief only.
- If the relationship API itself looks missing/insufficient, say so and escalate; don't design it
  here.

## On completion

- Set Status to `Done`.
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - `WI-P1-02` — implement player-relationship setup in `OnNewGame` per the brief (effort permitting,
    may need a stronger model).

## Notes

<!-- Append findings here. -->
