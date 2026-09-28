# Task Card `WI-C4g-03`: Import NIF morph targets

| Field | Value |
|---|---|
| Card ID | `WI-C4g-03` |
| Roadmap phase | [Track C · C4g](../../track-c-external-assets.md): EE2 morph animation |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4g-01`, `WI-C4g-02` |
| Diagrams | [C4g architecture diagrams](../../deep-dives/c4g-ee2-morph-animation.md#10-architecture-diagrams) |

## Goal

Convert EE2 NIF morph data into C6 morph target sets for embedded model controllers and
`<model>#morph-kf=<path>` paired KF target data.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4g-ee2-morph-animation.md` sections 2, 5, 6, and 8.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 5 and 6.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifSceneConverter.cs`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifTransformConverter.cs`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifModelImporter.cs`.
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifImportTests.cs`.

## Steps

1. Change Status to `In progress`.
2. Add a `NifMorphConverter` that receives a target primitive, source vertex positions, and
   `NiMorphData`.
3. Verify morph 0 equals the target primitive's source positions before coordinate conversion.
4. Omit morph 0 and convert morphs 1..N into position deltas using the target primitive's linear
   source-to-engine transform.
5. Add deterministic target names `Target1..TargetN`, default weights 0, and allowed ranges
   `[0, 1]`.
6. Teach the NIF model importer to add embedded model morph target sets when the gate is enabled.
7. Add support for `<model>#morph-kf=<archive-relative-kf-path>` so KF-owned target sets are
   imported with the model before materialization.
8. Add installed-asset tests for `bld_farm_yam.nifcache` plus `bld_farm_yam.kf`: 720 base
   vertices, one imported target, 600 nonzero deltas, and max delta near 5.4214 source units.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Nif/NifMorphConverter.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/Nif/NifSceneConverter.cs`
- `src/Panopticon.Simulation/Content/Importing/Nif/NifModelImporter.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifImportTests.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/NifModelImporterTests.cs`

## Acceptance criteria

- [x] Morph 0 base mismatch fails with `InvalidDataException`.
- [x] `RelativeTargets = 0` fails with `NotSupportedException`.
- [x] `bld_farm_yam#morph-kf=Graphics/Building/bld_farm_yam.kf` imports one C6 target set.
- [x] Embedded morph controllers import target sets when their base vertices validate.
- [x] Feature-disabled morph model import produces a blocking diagnostic before GPU allocation.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Ee2NifImportTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not parse NIF 10.0.1.0 morph bodies.
- Do not bind or play morph weight animation in this card.
- Do not copy EE2 assets into the repository.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4g-04` is done, change `WI-C4g-05` to `Ready`.
- Append a result line to `.exchange`.
