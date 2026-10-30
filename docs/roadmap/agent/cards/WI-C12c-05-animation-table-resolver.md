# Task Card `WI-C12c-05`: Animation tables, fragments, and unit resolution

| Field | Value |
|---|---|
| Card ID | `WI-C12c-05` |
| Roadmap phase | [Track C · C12c](../../track-c-external-assets.md): SHOGUN 2 animation and composite soldiers |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-C12a-02`, `WI-C12c-02` |
| Diagrams | [C12 architecture diagrams](../../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams) |

## Goal

A `Shogun2AnimationResolver` resolves a unit key and slot such as the charge slot to weighted clip paths and a skeleton type, through the DB columns from `WI-C12a-02`, `animation_tables.txt`, and battle-configuration fragments.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 5, 6 (with the `WI-C12a-02` column results), 9, 11.

## Steps

1. Add `AnimationTableSet.Parse(text)` for the `version 1` table grammar (tables, `skeleton_type`, fragments, `mount_table`) and `FragmentFile.Parse(text)` for slot lines with weighted clip paths and `blend_in_time`. Unknown keywords throw with file and line.
2. Add `Shogun2AnimationResolver.Resolve(unitKey, slot, context)` returning clip paths with weights, blend-in times, and the skeleton type.
3. Add synthetic grammar tests and an Interactive test that parses every table and fragment file and resolves `ashigaru_inf_yari_ashigaru` combat-idle and charge slots and the horse idle.

## Files likely to change

- `src/Panopticon.FileFormats.Shogun2/AnimationTableSet.cs`, `FragmentFile.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/Shogun2/Shogun2AnimationResolver.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/Shogun2/`

## Acceptance criteria

- [ ] All 296 animation text files parse or each failure is listed with line.
- [ ] The proof unit resolves to `yari_combat_idle1` and `yari_charge`; the horse to an idle clip.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
SHOGUN2_PATH="/mnt/pit2/Network Storage/Transfer/Shogun 2 Complete Edition" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Shogun2AnimationTable"
```

## Out of scope / guardrails

- Clip blending at runtime.
- Rider and mount pairing (C9d).
- No SHOGUN 2 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C12c-06` to `Ready` if `WI-C12c-04` is `Done`.
- Append a result line to `.exchange`.
