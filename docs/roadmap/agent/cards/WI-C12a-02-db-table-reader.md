# Task Card `WI-C12a-02`: DB table reader and unit-binding columns

| Field | Value |
|---|---|
| Card ID | `WI-C12a-02` |
| Roadmap phase | [Track C · C12a](../../track-c-external-assets.md): SHOGUN 2 packs |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-C12a-01` |
| Diagrams | [C12 architecture diagrams](../../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams) |

## Goal

A schema-driven DB table reader resolves the yari ashigaru rows in the `units`, `battle_entities`, `mount_variants`, and `unit_stats_land` tables, and the columns that lead from a unit key to its `.unit_variant` files and animation table are recorded.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 6, 8 (RPFM schema reference), and 9.
- `src/Panopticon.FileFormats.Pack/` from `WI-C12a-01`.
- RPFM SHOGUN 2 schema entries for the four tables (MIT; port with credit in a file header comment).

## Steps

1. Add `DbTable.Read(stream, schema)` and `DbSchema` to `Panopticon.FileFormats.Pack`: `FD FE FC FF` marker, GUID, optional version marker, row count, then rows decoded by column type (bool, i32, f32, UTF-16 string, optional string). A row-length mismatch throws with table path and row index.
2. Add `Shogun2DbSchema` to new project `src/Panopticon.FileFormats.Shogun2` with the four table schemas by table version.
3. Write an Interactive test `Shogun2DbTableTests` that reads every file under `db\units_tables\`, `db\battle_entities_tables\`, `db\mount_variants_tables\`, and `db\unit_stats_land_tables\` exactly to end of data and finds `ashigaru_inf_yari_ashigaru`.
4. Record in deep dive section 6 which columns name the unit variant, the animation table or skeleton type, and the mount, with the observed values for the proof unit.

## Files likely to change

- `src/Panopticon.FileFormats.Pack/DbTable.cs`, `DbSchema.cs` (new)
- `src/Panopticon.FileFormats.Shogun2/` (new project): `Shogun2DbSchema.cs`; `Panopticon.sln`
- `test/Panopticon.Simulation.Tests/Content/Importing/Shogun2/` (new test), `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` section 6

## Acceptance criteria

- [ ] Synthetic tables with each column type round-trip; truncated rows throw with context.
- [ ] Interactive: all four tables read exactly; the proof unit rows resolve.
- [ ] Deep dive section 6 names the unit-to-variant and unit-to-animation-table columns.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
SHOGUN2_PATH="/mnt/pit2/Network Storage/Transfer/Shogun 2 Complete Edition" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Shogun2Db"
```

## Out of scope / guardrails

- Tables beyond the four named.
- Campaign data, `data_fots.pack` content beyond overrides.
- No SHOGUN 2 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick C12a in the checklist and update the README Track C progress row.
- Set `WI-C12c-05` to `Ready` if `WI-C12c-02` is `Done`.
- Append a result line to `.exchange`.
