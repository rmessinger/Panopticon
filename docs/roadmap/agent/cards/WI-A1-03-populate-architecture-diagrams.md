# Task Card `WI-A1-03`: Replace the A1 architecture-diagram placeholder with grounded diagrams

| Field | Value |
|---|---|
| Card ID | `WI-A1-03` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): Headless simulation host, sub-phase A1-1 |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-A1-01`, `WI-A1-02` |
| Diagrams | This card produces them: [A1 §5](../../deep-dives/a1-headless-host.md#5-architecture-diagrams) |

## Goal

Replace the `<<pending A1>>` placeholder class diagram in
[a1-headless-host.md §5](../../deep-dives/a1-headless-host.md#5-architecture-diagrams) with a class
diagram and at least one sequence diagram grounded in the real types found by the two audits. This
is the gate the [agent guide](../README.md#how-to-maintain-this-guide) requires before any A1
implementation card may be marked `Ready`.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a1-headless-host.md`: both audit tables produced by
  `WI-A1-01` and `WI-A1-02`. This is the primary input.
- `docs/roadmap/deep-dives/a1-headless-host.md` §2 and §5: the design the diagrams must depict, and
  the placeholder being replaced.
- `docs/roadmap/deep-dives/p9-unit-validation-ground.md` §6 architecture diagrams: an example of the
  grounded, non-placeholder style to match.
- `docs/roadmap/agent/MAINTAINING.md`: roadmap formatting rules (no bold or italic emphasis).

## Steps

1. Draw a `classDiagram` showing `SimulationHost`, the null-device seam interface named by
   `WI-A1-01` step 3, and their relationships to the existing `Game`, `IGameEngine`, `GameLogic`,
   and `AssetStore` types. Use the real member names recorded in the audit, and drop any
   `<<pending A1>>` stereotype from a type whose shape the audit now fixes.
2. Keep speculative A1-5 output types (`RunOutput`, `TieredRecording`, `PerformanceMetrics`) in the
   diagram, but mark them `<<A1-5>>` rather than `<<pending A1>>`, since their fields are specified
   in [a5-visualization.md §5](../../deep-dives/a5-visualization.md#5-run-comparison--divergence-localization).
3. Add a `sequenceDiagram` for one headless tick: host to stepper to `GameLogic.Update` to
   `Scene3D.LogicTick` to `PartitionCellManager.Update`, showing the presentation calls from the
   `WI-A1-02` audit as explicitly not invoked.
4. Add a second `sequenceDiagram` for headless startup: construct the null device, build the
   `AssetStore` with data-only loading, then construct the host, per the ordering in
   `Game.cs:510-600`.
5. Replace §5's placeholder prose and diagram with the above. Do not restate design rationale that
   §2 already carries; link to it.
6. Remove the `†` marker from the A1 row of the Phase resources table in
   [`track-a-foundations.md`](../../track-a-foundations.md) and add this phase's cards to that row's
   `Agent cards` column.

## Files likely to change

- `docs/roadmap/deep-dives/a1-headless-host.md`
- `docs/roadmap/track-a-foundations.md`

## Acceptance criteria

- [ ] §5 contains one class diagram and at least two sequence diagrams, all with valid Mermaid.
- [ ] No `<<pending A1>>` stereotype remains in the file.
- [ ] Every type and member named in the diagrams either exists in `src/` or is explicitly marked
      as an A1 sub-phase deliverable.
- [ ] The A1 row of the `track-a-foundations.md` Phase resources table no longer carries `†` and
      lists the A1 cards.
- [ ] No `.cs` file is modified by this card.

## Validation commands

```sh
# Documentation-only card. Confirm no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not invent types the audits did not justify. If the audit left the null-device surface
  `ambiguous`, depict only the members it did fix and mark the rest as open.
- Do not write implementation code.
- Do not use bold or italic emphasis anywhere in the edited roadmap files.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Change `WI-A1-04` and `WI-A1-05` from `Blocked` to `Ready`.
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
