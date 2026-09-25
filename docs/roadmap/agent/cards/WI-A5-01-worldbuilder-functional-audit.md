# Task Card `WI-A5-01`: Audit WorldBuilder's functional state and document gaps for B4a

| Field | Value |
|---|---|
| Card ID | `WI-A5-01` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): Scenario authoring & visualization, sub-phase A5-6 |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none (A5-6 is an early-start sub-phase with no A1 or A4 dependency, per [a5-visualization.md §7](../../deep-dives/a5-visualization.md#7-sequencing-notes)) |
| Diagrams | Not applicable: A5-6 is WorldBuilder tooling, not the headless-sim pipeline the A5 §8 diagrams cover. |

## Goal

Produce the authoritative statement of what WorldBuilder can and cannot author, save, load, and
play at normal map sizes, so B4a is not the first place basic gaps are discovered. This is the audit
half of A5-6. Read-only: no code changes.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a5-visualization.md` §6 (A5-6 row) and §7: what A5-6 owes B4a.
- `docs/roadmap/deep-dives/b4a-larger-maps.md`: what B4a will build on this baseline, so gaps are
  described in terms B4a can consume.
- `src/Panopticon.Tools.WorldBuilder.Core/`: the whole directory. It is small. `EditorMap.cs`
  (`CreateNew`, `Open`, `Save`, `SaveAs`, dirty tracking, undo stack), `NewMapFactory.cs`, the tool
  implementations (`HeightBrushTool`, `PaintSingleTileTool`, `PaintLargeTileTool`, `SpillPaintTool`,
  `MeshMoldTool`, `PlaceObjectTool`, `WaypointTool`, `SelectTool`, `DeleteTool`, `EyeDropperTool`),
  and the edit types (`HeightMapEdit`, `BlendTileEdit`, `ObjectPlacementEdit`).
- `src/Panopticon.Tools.WorldBuilder.Maui/Views/` and `ViewModels/`: which Core capabilities the UI
  actually exposes.

## Steps

1. Tabulate every authoring capability in `WorldBuilder.Core`, one row per tool or edit type. For
   each record: the capability, whether the UI exposes it, whether it participates in undo and redo,
   and whether its result is written by `MapFile.WriteTo`.
2. Identify capabilities present in Core but not reachable from the UI, and UI affordances with no
   Core implementation. These are the two most actionable gap classes.
3. Trace the author to save to load to play round-trip through `EditorMap.CreateNew`,
   `EditorMap.Save`, `EditorMap.Open`, and the companion `map.ini` and `map.str` writing. Record any
   step that is not implemented or is implemented only partially.
4. Record what a newly created map lacks before the engine can play it: player start positions,
   waypoints, team and script data, and the sides list. Base this on `NewMapFactory` and the map
   data types it populates, not on speculation.
5. Note any capability whose implementation is obviously size-dependent, since B4a will scale it.
   Describe the dependency; do not measure or benchmark.
6. Create `docs/roadmap/implementation/track-a-a5-6-worldbuilder-baseline.md` following the
   structure of `docs/roadmap/implementation/track-g-g1-high-dpi.md` (human-facing summary above the
   `---` divider, agent-context detail below) and write the capability table and the gap list below
   the divider.
7. Add a `## Track A · A5-6 : WorldBuilder baseline` row group for the new plan in
   `docs/roadmap/implementation/README.md`.

## Files likely to change

- `docs/roadmap/implementation/track-a-a5-6-worldbuilder-baseline.md` (new)
- `docs/roadmap/implementation/README.md`

## Acceptance criteria

- [ ] Every tool and edit type in `WorldBuilder.Core` appears in the capability table.
- [ ] Core-without-UI and UI-without-Core gaps are listed explicitly.
- [ ] Each step of the author to save to load to play round-trip is marked implemented, partial, or
      missing, with the file and symbol backing the verdict.
- [ ] The gaps are phrased so B4a can consume them directly.
- [ ] No `.cs` file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not fix any gap found. `WI-A5-02` adds the round-trip test; other fixes get their own cards.
- Do not scale anything or touch map-size limits. That is B4a.
- Do not assess the MAUI UI's visual design; assess only capability reachability.
- Do not verify behavior by running the tool interactively; classify from the code. Runtime
  verification is `WI-A5-02`'s job.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`, including the count of missing round-trip steps.
- Follow-up cards to create:
  - One card per missing or partial round-trip step, in the order the round-trip executes.
