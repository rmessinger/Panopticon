# Task Card `WI-A2-02`: Audit non-random nondeterminism on the logic path

| Field | Value |
|---|---|
| Card ID | `WI-A2-02` |
| Roadmap phase | [Track A · A2](../../track-a-foundations.md): Statistical validation |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none (independent of `WI-A2-01`) |
| Diagrams | None: A2 has no deep dive yet. This card is one of the investigations that produces the material for one. |

## Goal

List every non-random source of run-to-run variation on the simulation path: wall-clock reads,
unordered collection iteration, hash-code-dependent ordering, identity-based ordering, and
concurrency. This is the "audit uncontrolled nondeterminism" half of A2's first checklist item.
Read-only: no code changes.

## Context to read (and only this)

- `docs/roadmap/track-a-foundations.md`: the A2 phase row.
- `docs/roadmap/open-questions.md`: the determinism-priority Decision.
- `src/OpenSage.Game/Game.cs:914-1000`: the tick path whose reachable code is in scope.
- `src/OpenSage.Game/Logic/GameLogic.cs`: object storage and iteration during the logic update.
- `src/OpenSage.Game/Logic/Player.cs`: player and owned-object collections iterated per tick.

## Steps

1. Grep the logic path for wall-clock and environment reads: `DateTime.Now`, `DateTime.UtcNow`,
   `Environment.TickCount`, `Stopwatch`, `Guid.NewGuid`.
2. Grep the logic path for order-unstable iteration: `foreach` over `Dictionary<`, `HashSet<`, and
   `.ToHashSet(`, plus any `OrderBy` keyed on a reference or hash code.
3. Grep the logic path for concurrency: `Parallel.`, `Task.Run`, `lock (`, and `async` methods
   reachable from the tick.
4. For each hit record: `file:line`, the category, whether it is reachable from `GameLogic.Update`,
   `Scene3D.LogicTick`, or scripting, and whether its result can change simulation state or only
   presentation or logging.
5. Rank the simulation-affecting entries by blast radius: entity-order-dependent combat or
   pathfinding resolution ranks above a logged timestamp.
6. Write the table into a `## Non-random nondeterminism audit` section of
   `docs/roadmap/implementation/track-a-a2-reproducibility.md`, below the `---` divider. Create that
   file per `WI-A2-01` step 5 if it does not yet exist.
7. Record, without deciding, which entries would need the concurrent-write conflict-resolution rules
   that A2's second checklist item calls for. Deciding those rules is a deep-dive task, not this
   card's.

## Files likely to change

- `docs/roadmap/implementation/track-a-a2-reproducibility.md`
- `docs/roadmap/implementation/README.md` (only if `WI-A2-01` has not already added the row)

## Acceptance criteria

- [ ] Every wall-clock, unordered-iteration, and concurrency hit reachable from the tick path
      appears in the table with a category and a reachability verdict.
- [ ] Simulation-affecting entries are ranked by blast radius.
- [ ] Candidates for concurrent-write conflict rules are listed without a rule being invented.
- [ ] No `.cs` file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not fix anything found, however small.
- Do not treat run-to-run variation as a defect without an independent correctness violation.
- Do not define the conflict-resolution rules; escalate that to a deep dive.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the A2 experiment-controls audit clause in
  [`implementation-checklist.md`](../../implementation-checklist.md) once both `WI-A2-01` and
  `WI-A2-02` are `Done`.
- Append a result line to repo-root `.exchange`, including the count of simulation-affecting
  entries.
- Follow-up cards to create:
  - An A2 deep dive covering seed controls, statistical validation, and concurrent-write
    correctness. It is a high-effort design task, not a card.
