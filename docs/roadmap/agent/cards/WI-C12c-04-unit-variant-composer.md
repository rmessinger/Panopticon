# Task Card `WI-C12c-04`: `.unit_variant` reader and seeded composer

| Field | Value |
|---|---|
| Card ID | `WI-C12c-04` |
| Roadmap phase | [Track C · C12c](../../track-c-external-assets.md): SHOGUN 2 animation and composite soldiers |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-C12c-03` |
| Diagrams | [C12 architecture diagrams](../../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams) |

## Goal

A `Shogun2UnitVariantComposer` reads version 2 `.unit_variant` files and composes one merged skinned `ImportedModel` per soldier from a seed, so the same seed always yields the same parts and textures.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 6, 9 (composition decision), 11 (composite sequence).
- `src/Panopticon.Simulation/Content/Importing/ImportedModel.cs`, `ImportedSubObject.cs`.

## Steps

1. Add `VrntFile.Read(stream)` for version 2 (slots, weighted `mesh` and `tex/texture` choices, UTF-16 paths); version 0 throws with path.
2. Add `Shogun2UnitVariantComposer.Compose(variantPath, skeleton, seed, context)`: per slot, pick a weighted choice with `System.Random` seeded from unit key, role, and soldier index; import each part through `Rmv2ModelImporter`, override its texture directory when the choice names one, and merge parts as sub-objects on one skeleton.
3. Cache imported parts per path within one composition batch so a squad imports each part once.
4. Add tests: synthetic variant weights produce the expected distribution over many seeds, and the same seed composes identically.
5. Add an Interactive test composing 16 yari ashigaru soldiers and recording distinct composition count.

## Files likely to change

- `src/Panopticon.FileFormats.Shogun2/VrntFile.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/Shogun2/Shogun2UnitVariantComposer.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/Shogun2/`

## Acceptance criteria

- [ ] 974 version 2 variants parse or each failure is listed.
- [ ] Same seed, same composition; 16 soldiers produce more than one distinct composition.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
SHOGUN2_PATH="/mnt/pit2/Network Storage/Transfer/Shogun 2 Complete Edition" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Shogun2Variant"
```

## Out of scope / guardrails

- Officer and standard-bearer roles beyond parsing.
- Colour-mask tinting.
- Mesh merging into one draw call (B5 owns batching).
- No SHOGUN 2 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C12c-06` to `Ready` if `WI-C12c-05` is `Done`.
- Append a result line to `.exchange`.
