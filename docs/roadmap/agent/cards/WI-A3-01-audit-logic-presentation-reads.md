# Task Card `WI-A3-01`: Audit logic reads of render-only and audio-only state

| Field | Value |
|---|---|
| Card ID | `WI-A3-01` |
| Roadmap phase | [Track A · A3](../../track-a-foundations.md): Simulation/presentation seam |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | None: A3 has no deep dive yet. This card produces the material for one. |

## Goal

Produce the authoritative list of every place simulation logic reads presentation-only state,
`Drawable`, camera, render, audio, cursor, or GUI, so the A3 boundary can be defined and enforced.
This is A3's only checklist item's first half. Read-only: no code changes.

## Context to read (and only this)

- `docs/roadmap/track-a-foundations.md`: the A3 phase row and the note that A3 coordinates with C1
  because both touch the presentation seam.
- `src/OpenSage.Game/Logic/`: the logic namespace, the audit's subject.
- `src/OpenSage.Game/Logic/GameLogic.cs`: including the known `gameObject.Drawable?.Destroy()` call,
  which must appear in the audit.
- `src/OpenSage.Game/Logic/Object/GameObject.cs`: the `Drawable` property, the main coupling point.
- `src/OpenSage.Game/Game.cs:985-997`: `LogicTick`, which defines what "during a logic tick" means.

## Steps

1. Grep `src/OpenSage.Game/Logic/` for references to presentation types: `Drawable`, `Camera`,
   `GraphicsDevice`, `Scene2D`, `AudioSystem`, `Cursors`, `RenderTime`, `ModelInstance`,
   `W3d`-prefixed types, and the `OpenSage.Rendering` namespace.
2. For each hit record: `file:line`, the logic type, the presentation type referenced, and a
   classification of `read` (logic queries presentation state and branches on it), `write` (logic
   commands presentation, which is permitted under the seam), or `type-only` (a signature or field
   declaration with no runtime access).
3. Flag every `read`: these are the actual A3 violations. `write` and `type-only` entries are
   context, not defects.
4. For each `read`, note whether the value it needs already exists on the logic side or would have
   to be mirrored across the seam.
5. Note the `Logic/Object/Draw/` module tree separately. Its `DrawModule` types live under `Logic/`
   by namespace but are presentation by role; classify them as such and do not count them as
   violations.
6. Create `docs/roadmap/implementation/track-a-a3-sim-presentation-seam.md` following the structure
   of `docs/roadmap/implementation/track-g-g1-high-dpi.md` (human-facing summary above the `---`
   divider, agent-context detail below) and write the table into a `## Logic-to-presentation read
   audit` section below the divider.
7. Add a `## Track A · A3 : Simulation/presentation seam` row group for the new plan in
   `docs/roadmap/implementation/README.md`.

## Files likely to change

- `docs/roadmap/implementation/track-a-a3-sim-presentation-seam.md` (new)
- `docs/roadmap/implementation/README.md`

## Acceptance criteria

- [ ] Every presentation-type reference under `src/OpenSage.Game/Logic/` appears in the table with a
      classification.
- [ ] Every `read` entry states whether the needed value already exists logic-side.
- [ ] The `Logic/Object/Draw/` module tree is called out as presentation-by-role, not as violations.
- [ ] No `.cs` file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not fix any violation found, however small.
- Do not define the seam interface or propose a refactor: that needs an A3 deep dive.
- Do not audit the reverse direction. Presentation reading logic state is expected and permitted.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`, including the count of `read` violations.
- Follow-up cards to create:
  - An A3 deep dive defining the boundary and its architecture diagrams. It is a high-effort design
    task, not a card.
