# Task Card `WI-A5-34`: Persist the map.ini and map.str companions

| Field | Value |
|---|---|
| Card ID | `WI-A5-34` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-6 WorldBuilder baseline |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-28` (companions for new maps only matter once new maps save) |
| Diagrams | Not applicable: bounded WorldBuilder baseline repair. |

## Goal

Make `EditorMap.Save` write the `map.ini` and `map.str` companion files next to the `.map` file
and `EditorMap.Open` load them, closing the missing companions step of the audit
([baseline](../../implementation/track-a-a5-6-worldbuilder-baseline.md)).

## Context to read (and only this)

- The companions rows of the WorldBuilder baseline audit.
- `src/Panopticon.Tools.WorldBuilder.Core/EditorMap.cs` (`Save`, `Open`, `MapIniContent`,
  `MapStrContent`).
- `src/Panopticon.Tools.WorldBuilder.Avalonia/Views/MapIniWindow.cs` and
  `MapStrEditorWindow.cs` (the editing surfaces).

## Steps

1. On `Save`, write `MapIniContent` to `<mapPath>.ini` and `MapStrContent` to
   `<mapPath>.str` when non-empty; the `.map` stream stays authoritative for map data.
2. On `Open`, read the companions when the files exist, else default the strings.
3. `SaveAs` writes companions under the new path; a stale companion at the old path is left in
   place.
4. Add Core-level tests for save-then-open round-trip of both companions.

## Acceptance criteria

- [x] Save writes both companions; open restores both strings; absent files default cleanly.
- [x] The `.map` stream content is unchanged by this card.
  - `Save` still writes the `MapFile` stream first; companions are additive writes beside it.
- [x] Core tests cover the round-trip without the UI.
  - `MapCompanionTests` 3/3: round-trip, absent-defaults, and `SaveAs` retargeting with the
    stale first companion left in place.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).
