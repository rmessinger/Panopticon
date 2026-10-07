# Task Card `WI-G1-01`: Audit window/panel size consumers and classify points vs pixels

| Field | Value |
|---|---|
| Card ID | `WI-G1-01` |
| Roadmap phase | [Track G · G1: High-DPI drawable & resolution correctness](../../track-g-rendering-clarity.md) |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [G1 Architecture Diagrams](../../deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams) |

## Goal

Produce the authoritative classification of every consumer of window or panel size as needing
logical points or physical pixels. Cards `WI-G1-02` through `WI-G1-04` act on this table, so it
must be complete before they run. Read-only: no code changes.

## Context to read (and only this)

- `docs/roadmap/deep-dives/g1-high-dpi-resolution.md`: the design, particularly §3 and the
  appendix, which already lists the known call sites.
- `src/Panopticon.Simulation/GameWindow.cs:36-43,73-129`: `ClientBounds`, the constructor, and `HandleResized`.
- `src/Panopticon.Simulation/GamePanel.cs`: `Frame`, `ClientBounds`, `EnsureFrame`.
- `src/Panopticon.Simulation/Game.cs:630-645,666-683`: the `EnsureFrame` call and `OnPanelSizeChanged`.
- `src/Panopticon.Simulation/Graphics/Cameras/Camera.cs:80-90,140-200`: `ScreenSize`, `Unproject`, `Project`.

## Steps

1. Search the repository for every read of `GameWindow.ClientBounds`, `GameWindow.WindowScale`,
   `GameWindow.CursorScale`, `GamePanel.ClientBounds`, `GamePanel.Frame`, and `Game.Viewport`.
   Use `grep`; do not open files outside the hits and the context list above.
2. For each hit record: file:line, the consuming subsystem, and the classification `points`,
   `pixels`, or `ambiguous`. Classify by what the value is ultimately used for: allocating a GPU
   surface or setting a viewport is `pixels`; laying out GUI, hit-testing mouse input, or sizing a
   cursor is `points`.
3. Flag every site where a single value currently feeds both classifications. `OnPanelSizeChanged`
   is known to be one; report any others found.
4. Write the table into a new `## Consumer classification` section of
   `docs/roadmap/implementation/track-g-g1-high-dpi.md`, below the `---` divider (the agent-context
   half of the plan).
5. If any consumer cannot be classified from the listed context, mark it `ambiguous` with a one-line
   reason rather than guessing.

## Files likely to change

- `docs/roadmap/implementation/track-g-g1-high-dpi.md`

## Acceptance criteria

- [x] Every `ClientBounds` / `WindowScale` / `CursorScale` / `Frame` / `Viewport` read in `src/` appears
      in the table with a classification.
- [x] Sites feeding both classifications are called out explicitly.
- [x] No `.cs` file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not change any code, including "obvious" fixes.
- Do not add the `AllowHighDpi` flag or the pixel-size interop: that is `WI-G1-02`.
- Do not classify by guessing at runtime behaviour on a scaled display; classify by use.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`, including the count of `ambiguous` entries.
- Follow-up cards to create:
  - None; `WI-G1-02` through `WI-G1-04` already exist and depend on this table.
