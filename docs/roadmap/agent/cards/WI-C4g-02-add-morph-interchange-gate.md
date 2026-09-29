# Task Card `WI-C4g-02`: Add morph interchange gate

| Field | Value |
|---|---|
| Card ID | `WI-C4g-02` |
| Roadmap phase | [Track C · C4g](../../track-c-external-assets.md): EE2 morph animation |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C4c-06` |
| Diagrams | [C4g architecture diagrams](../../deep-dives/c4g-ee2-morph-animation.md#10-architecture-diagrams) |

## Goal

Add the C6 morph-target interchange types and request-scoped `morph-targets` gate without changing
feature-disabled model output.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4g-ee2-morph-animation.md` sections 1, 6, 7, and 8.
- `docs/roadmap/deep-dives/c6-extended-model-features.md` sections 1 through 4.
- `src/Panopticon.Simulation/Content/Importing/ImportedMesh.cs`.
- `src/Panopticon.Simulation/Content/Importing/ImportedVertex.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelImportOptions.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelPreviewOptions.cs`.

## Steps

1. Change Status to `In progress`.
2. Add imported morph types: `ImportedMorphTargetSet`, `ImportedMorphTarget`, and
   `ImportedMorphAnimationChannel`.
3. Add a stable primitive identifier to imported meshes or mesh parts, preserving existing
   constructors through defaults where possible.
4. Add morph target sets to `ImportedMesh` with validation for finite values, matching counts,
   target names, default weights, and allowed ranges.
5. Add morph animation channels to `ImportedAnimation`, separate from bone channels.
6. Wire the existing ReplaySketch morph gate name to the C4/C6 import options used by materializers.
7. Add feature-disabled tests that import existing C4a/C4b assets and assert no morph payload is
   present unless the gate is enabled.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/ImportedMesh.cs`
- `src/Panopticon.Simulation/Content/Importing/ImportedAnimation.cs`
- `src/Panopticon.Simulation/Content/Importing/ImportedMorphTargetSet.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/ImportedMorphTarget.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/ImportedMorphAnimationChannel.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelImportOptions.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedAssetTests.cs`

## Acceptance criteria

- [x] Imported morph target sets validate counts, finite deltas, default weights, ranges, and the
      eight-target limit.
- [x] Imported morph animation channels validate target names, time order, and weight vector length.
- [x] Feature-disabled imports preserve existing C4a/C4b descriptors.
      Synthetic coverage: mesh/animation default to no morph payload and empty primitive id.
      Installed galley/bomber no-morph assertions land with `WI-C4g-03`.
- [x] Morph payloads require the `morph-targets` gate before materialization.
      `ImportedModelMaterializer` refuses morph-carrying models naming the gate; runtime
      materialization arrives with `WI-C4g-04`.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ImportedAssetTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add GPU shader morphing.
- Do not widen the skinning influence count.
- Do not add glTF morph import.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4g-01` is done, change `WI-C4g-03` to `Ready`.
- If done, change `WI-C4g-04` to `Ready`.
- Append a result line to `.exchange`.
