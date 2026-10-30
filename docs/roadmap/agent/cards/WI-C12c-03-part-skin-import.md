# Task Card `WI-C12c-03`: `VMPF` skin import

| Field | Value |
|---|---|
| Card ID | `WI-C12c-03` |
| Roadmap phase | [Track C · C12c](../../track-c-external-assets.md): SHOGUN 2 animation and composite soldiers |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-C12b-04`, `WI-C12c-02` |
| Diagrams | [C12 architecture diagrams](../../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams) |

## Goal

`VMPF` parts import as skinned meshes with up to four influences, bound by bone name to the clip skeleton for their skeleton type.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 4, 5, and the results of `WI-C12b-01`.
- `src/Panopticon.Simulation/Content/Importing/ImportedVertex.cs` (bone indices and weights), `ImportedModel.cs` (`ImportedSkeletonOrigin`).
- `src/Panopticon.Simulation/Content/Importing/Nif/NifSkinConverter.cs` (precedent).

## Steps

1. Map `VMPF` influences to `ImportedVertex` bone indices and weights, normalizing weights and keeping the four largest when more are present (count recorded).
2. Resolve part bone references against the skeleton from `WI-C12c-02` by name; unknown bones fail with part path and bone name.
3. Add an Interactive test that binds the proof head part and horse body to `yari_combat_idle1` and `horse_combat_idle_01` with no missing bones, and that a posed frame moves vertices.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Shogun2/Rmv2ModelImporter.cs` (skin path)
- `test/Panopticon.Simulation.Tests/Content/Importing/Shogun2/`

## Acceptance criteria

- [ ] Proof parts import skinned with weights summing to 1.
- [ ] Parts with more than four influences are counted and recorded.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
SHOGUN2_PATH="/mnt/pit2/Network Storage/Transfer/Shogun 2 Complete Edition" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Shogun2Skin"
```

## Out of scope / guardrails

- Composition (`WI-C12c-04`).
- No SHOGUN 2 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C12c-04` to `Ready`.
- Append a result line to `.exchange`.
