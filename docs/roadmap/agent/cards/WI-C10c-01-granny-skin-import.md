# Task Card `WI-C10c-01`: Granny bone bindings as four-influence skins

| Field | Value |
|---|---|
| Card ID | `WI-C10c-01` |
| Roadmap phase | [Track C · C10c](../../track-c-external-assets.md): AoE3 DE skinned animation |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-C10b-02` |
| Diagrams | [C10 architecture diagrams](../../deep-dives/c10-aoe3de-granny-models.md#9-architecture-diagrams) |

## Goal

Skinned Granny meshes import with per-vertex bone indices and weights mapped through `BoneBindings` to skeleton bones, up to four influences.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` sections 1 and 3.
- `GrannyMeshConverter.cs`, `ImportedVertex.cs` (four indices and weights), `docs/roadmap/deep-dives/c4d-ee2-skinned-animation.md` (four-influence precedent).

## Steps

1. Map `BoneBindings` names to skeleton indices; a binding with no matching bone fails with mesh and bone name.
2. Read `BoneIndices` and `BoneWeights` (byte-normalized), remap through the bindings, drop zero weights, keep the four largest, and renormalize.
3. Set `IsSkinned` and leave rigid single-binding meshes as in `WI-C10b-02`.
4. Interactive test: musketeer and hussar horse report influence histograms and max weight-sum error.

## Files likely to change

- `GrannyMeshConverter.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Aoe3/GrannySkinImportTests.cs` (new)

## Acceptance criteria

- [ ] Weight sums are 1 within 1e-3 for every skinned vertex.
- [ ] Bind-pose skinned positions equal the static import within 1e-3.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
AOE3DE_PATH="/mnt/pit2/Network Storage/Transfer/AoE3DE" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Aoe3"
```

## Out of scope / guardrails

- More than four influences.
- Clip import (`WI-C10c-02`).
- No AoE3 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C10c-04` to `Ready` if `WI-C10c-02`, `WI-C10c-03`, and `WI-C10b-04` are done.
- Tick C10c in the checklist when its last card is done, and update the README progress row.
- Append a result line to `.exchange`.
