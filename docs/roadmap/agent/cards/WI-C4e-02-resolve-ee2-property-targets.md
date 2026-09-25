# Task Card `WI-C4e-02`: Resolve EE2 property targets

| Field | Value |
|---|---|
| Card ID | `WI-C4e-02` |
| Roadmap phase | [Track C · C4e](../../track-c-external-assets.md): EE2 property animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4c-02` |
| Diagrams | [C4e architecture diagrams](../../deep-dives/c4e-ee2-property-animation.md#11-architecture-diagrams) |

## Goal

Expose NIF source identity for sub-objects and material slots so C4e controllers can resolve target names and embedded target refs without guessing.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4e-ee2-property-animation.md` sections 2, 5.2, 5.4, and 6.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` section 5.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifSceneConverter.cs`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifMaterialConverter.cs`.
- `src/Panopticon.Simulation/Content/Importing/ImportedSubObject.cs` and `ImportedMeshPart.cs`.
- `test/Panopticon.Simulation.Tests/Content/Importing/NifModelImporterTests.cs`.

## Steps

1. Add an internal NIF scene descriptor returned beside the imported model data. It must map source `NiNode`, `NiTriShape`, `NiTexturingProperty`, and `NiMaterialProperty` block indices to imported bone names, sub-object full names, and material slot ids.
2. Preserve enough source naming to resolve controlled-block strings like `<shape-name>\nPROP\nNiMaterialProperty` and `<shape-name>\nPROP\nNiTexturingProperty` case-insensitively.
3. Resolve embedded controller target refs by finding every imported material slot that uses the referenced property block.
4. Add ambiguity detection for duplicate shape/property targets. Ambiguous targets must fail instead of picking the first match.
5. Add synthetic model-converter tests for one node visibility target, one shape visibility target, one texturing-property target, one material-property target shared by two shapes, and one ambiguous duplicate.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Nif/NifSceneConverter.cs`
- `src/Panopticon.Simulation/Content/Importing/Nif/NifModelImporter.cs`
- `src/Panopticon.Simulation/Content/Importing/Nif/NifMaterialConverter.cs`
- `src/Panopticon.Simulation/Content/Importing/Nif/NifPropertyTargetMap.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/NifModelImporterTests.cs`

## Acceptance criteria

- [ ] A NIF property target map identifies every imported sub-object and material slot needed by C4e.
- [ ] Controlled-block property strings and embedded property refs resolve to the same slot ids in synthetic tests.
- [ ] Shared properties produce deterministic multi-slot results.
- [ ] Missing or ambiguous property targets fail with asset context.
- [ ] Existing C4c rest-pose parity tests pass unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifModelImporterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add new animation channel types.
- Do not change material rendering.
- Do not parse DDF files.
- Do not change C4c bone construction except where source identity must be retained.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C4e-01` is done, change `WI-C4e-03` to `Ready`.
- If done and `WI-C4e-01` and `WI-C4e-03` are done, change `WI-C4e-04` to `Ready`.
- Append a result line to `.exchange`.
