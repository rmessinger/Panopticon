# Task Card: `P1-02` — Implement player-relationship setup in `OnNewGame`

| Field | Value |
|---|---|
| **Card ID** | `P1-02` |
| **Roadmap phase** | [Track P · P1 — Skirmish setup & player relationships](../../track-p-playable-baseline.md) (sub-phase **P1-1**, see [deep dive §3.1](../../deep-dives/skirmish-setup.md)) |
| **Status** | `Ready` |
| **Type** | `pattern-replication` |
| **Effort** | `S` |
| **Prerequisites** | none |

## Goal

Implement the `// TODO: Setup player relationships.` in `PlayerManager.OnNewGame()` and make
`Player.AddAlly`/`AddEnemy` **reciprocal**, so that after players are created every pair of players
has a correct, symmetric relationship (`A enemy B ⇒ B enemy A`). This is the core P1 fix that the AI
(P2), victory/defeat (P3), and combat (P6) all depend on. **Scope is the relationship wiring only** —
the SP/MP spawn unification and `LocalPlayer` selection are a *separate* card (out of scope below).

## Context to read (and *only* this)

- `src/OpenSage.Game/Logic/PlayerManager.cs:24-76` — `OnNewGame()` (the TODO at line 45) and
  `CreatePlayers()` (which already maps map-defined `Allies`/`Enemies` into the `Player` sets).
- `src/OpenSage.Game/Logic/Player.cs:124-139` — `Allies`/`Enemies` (`HashSet<Player>`) and `Team`.
- `src/OpenSage.Game/Logic/Player.cs:779-786` — `AddAlly`/`AddEnemy` (currently add to the local set
  only, **no reciprocal wiring**).
- `src/OpenSage.Game/Logic/Player.cs:811-831` — `GetRelationship(Team? that)` (team override → player
  override → neutral); confirm it already consumes the sets/teams you populate.
- `src/OpenSage.Game/Logic/Player.cs:965-994` — `RelationshipType { Enemies=0, Neutral=1, Allies=2 }`
  and the `PlayerRelationships` override map.

## Steps

1. **Make wiring reciprocal.** In `Player.AddAlly(Player player)` and `AddEnemy(Player player)`
   (`Player.cs:779-786`), after adding `player` to this player's set, also add `this` to the other
   player's corresponding set — guarding against duplicate/self entries (don't ally a player with
   itself; `HashSet` already dedupes). Keep the methods idempotent.
2. **Populate relationships in `OnNewGame`.** Replace the `// TODO: Setup player relationships.`
   (`PlayerManager.cs:45`) with logic that, for each ordered pair of non-neutral players, derives the
   relationship from their `Team`: **same `Team` ⇒ allies, different `Team` ⇒ enemies**. The neutral
   player (empty name `""`, see `CreatePlayers`) stays **neutral** to everyone (add no ally/enemy
   entries for it). Use the now-reciprocal `AddAlly`/`AddEnemy` so each pair is wired once.
3. **Respect existing map relationships.** `CreatePlayers` already fills `Allies`/`Enemies` from the
   map. Your team-derived wiring should *augment* those sets (additive), not clear them — only add the
   missing skirmish-slot-driven relationships. Do not remove map-defined entries.
4. Do **not** touch `LocalPlayer` selection (lines 28-43) or any spawn/camera code.

## Files likely to change

- `src/OpenSage.Game/Logic/PlayerManager.cs` — `OnNewGame()` body.
- `src/OpenSage.Game/Logic/Player.cs` — `AddAlly`/`AddEnemy` reciprocity only.

## Acceptance criteria

- [ ] `Player.AddAlly(b)` results in `a.Allies.Contains(b)` **and** `b.Allies.Contains(a)`; same for
      `AddEnemy`/`Enemies`. Calling twice does not duplicate or throw.
- [ ] After `OnNewGame`, two players on the **same** `Team` are mutual allies; two on **different**
      teams are mutual enemies; the neutral player is in nobody's ally/enemy set.
- [ ] Map-defined `Allies`/`Enemies` from `CreatePlayers` are preserved (not cleared).
- [ ] `GetRelationship(...)` returns `Allies`/`Enemies`/`Neutral` consistent with the above for a
      sample pair.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- **Do NOT** change `LocalPlayer` selection or unify the SP/MP spawn path — those are open design
  questions (↗) in the [deep dive §5](../../deep-dives/skirmish-setup.md). If your change starts
  needing them, mark `Blocked` and report.
- Do not redesign the relationship API or the `Team` model — only wire what exists.
- Keep the diff to the two files above.

## On completion

- Set **Status** above to `Done` (or `Blocked` + reason).
- Update the **P1 row Status** in [`track-p-playable-baseline.md`](../../track-p-playable-baseline.md)
  to `In progress` if not already.
- Tick the matching item(s) in the [implementation checklist](../../implementation-checklist.md) (P1-1).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - [`P1-03`](P1-03-test-reciprocal-relationships.md) — unit test for the reciprocal wiring (if not
    already present).
  - A card (may need a stronger model) for **P1-2** unified SP/MP spawn + `LocalPlayer` selection,
    once the ↗ decisions in [deep dive §5](../../deep-dives/skirmish-setup.md) are made.
