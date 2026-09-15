# Task Card `WI-C4-11`: Add the glTF fixture matrix

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C4-11` |
| Roadmap phase | [Track C · C4](../../track-c-external-assets.md): First production external format |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4-10` |
| Diagrams | [C4 architecture diagrams](../../deep-dives/c4-gltf-importer.md#11-architecture-diagrams) |

## Goal

Commit a tiny project-authored `.gltf` and `.glb` CPU fixture matrix that locks supported behavior
and documented exclusions without test-time downloads.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4-gltf-importer.md` sections 1 and 10.
- `src/OpenSage.Game.Tests/OpenSage.Game.Tests.csproj`: copied test-data patterns.
- `src/OpenSage.Game.Tests/Content/Importing/W3dModelImporterTests.cs`: importer descriptor style.
- `src/OpenSage.Game.Tests/Content/Importing/W3dAnimationImporterTests.cs`: fixed-time animation
  assertions.
- `src/OpenSage.Game/Content/Importing/Gltf/GltfModelImporter.cs` (new from `WI-C4-10`).
- `src/OpenSage.Game/Content/Importing/Gltf/GltfAnimationImporter.cs` (new from `WI-C4-08`).
- `src/OpenSage.Game.Tests/Content/Importing/GltfImporterTests.cs` (new from `WI-C4-10`).
- `src/OpenSage.Game.Tests/Content/Importing/GltfAnimationImporterTests.cs` (new from `WI-C4-08`).
- `src/OpenSage.Game.Tests/Content/Importing/PbrImportedModelTestData.cs` (new from `WI-C3-10`).

## Steps

1. Add a fixture README that states every file is project-authored, lists the expected feature or
   failure, and gives fixture-update instructions using a locally installed official Khronos
   glTF Validator. Record the validator version in update results; do not invoke or download it
   during tests.
2. Add a deterministic fixture generator using SharpGLTF and ImageSharp with fixed geometry,
   encoder settings, ordering, and metadata. Its opt-in fixture-generation test writes to an
   explicitly supplied output directory; ordinary tests generate into two temporary directories,
   compare every byte, and compare generated output with committed fixtures.
3. Add equivalent external-buffer `.gltf` and embedded `.glb` static triangles, plus a data-URI
   triangle.
4. Add accessor, rigid-scene, and PBR fixtures covering interleaving, normalized values, sparse
   overlay, unsigned indices, multiple primitives, transforms, one-meter conversion, supported
   scale, bounds, UV0, UV1, PNG, JPEG, samplers, alpha, and double-sided materials. Give the
   rigid-scene fixture one named unskinned mesh node with translation and rotation animation.
5. Add two-influence skin and animated-skin fixtures with non-identity rest transforms,
   non-identity inverse binds, differently ordered named bones, and linear and step channels.
6. Add minimal standalone exclusion fixtures for morph targets, a meaningful third influence,
   multiple skins, cubic interpolation, a required unsupported extension, a mirrored node, and a
   malformed accessor.
7. Copy fixtures to test output through the test project and add one data-driven CPU test suite.
   Compare complete deterministic descriptors where practical and exact exception type plus
   contextual identifier for exclusions.
8. Assert fixture tests perform no network access and use only a test-owned virtual or disk file
   system rooted at the committed fixture directory.

## Files likely to change

- `src/OpenSage.Game.Tests/OpenSage.Game.Tests.csproj`
- `src/OpenSage.Game.Tests/Content/Importing/GltfFixtureGenerator.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/GltfFixtureGeneratorTests.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/GltfFixtureTests.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/README.md` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/external-triangle.gltf` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/external-triangle.bin` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/data-uri-triangle.gltf` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/static-triangle.glb` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/accessor-layouts.gltf` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/accessor-layouts.bin` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/rigid-scene.gltf` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/rigid-scene.bin` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/pbr-materials.gltf` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/pbr-materials.bin` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/base-color.png` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/emissive.jpg` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/two-influence-skin.gltf` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/two-influence-skin.bin` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/animated-skin.glb` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/unsupported-morph.gltf` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/unsupported-third-influence.gltf` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/unsupported-multiple-skins.gltf` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/unsupported-cubicspline.gltf` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/unsupported-required-extension.gltf` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/unsupported-mirrored-node.gltf` (new)
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/malformed-accessor.gltf` (new)

## Acceptance criteria

- [ ] `.gltf` and `.glb` static triangles produce equivalent interchange descriptors.
- [ ] The matrix covers external, data-URI, and GLB resources and every committed accessor form.
- [ ] Scene, skin, animation, and PBR fixtures cover the complete positive CPU contract.
- [ ] Every exclusion fixture listed in step 5 asserts its exact exception type and contextual
      identifier; remaining excluded features stay covered by converter unit tests from
      `WI-C4-06` through `WI-C4-10`.
- [ ] Fixture bytes and images are project-authored and deterministic.
- [ ] Generating twice is byte-stable, and committed fixtures match generator output.
- [ ] Tests make no network request and do not download the validator.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GltfFixtureTests|FullyQualifiedName~GltfFixtureGeneratorTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not copy Khronos sample assets or other third-party model files into the repository.
- Do not run `npx`, `curl`, or package installation from a test.
- Do not add renderer assertions or screenshots; card 12 owns runtime proof.
- Do not weaken exclusions by accepting advanced features.
- Do not duplicate converter-level exclusion fixtures already covered by cards 6 through 10.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and all prerequisites of `WI-C4-12` are done, change `WI-C4-12` to `Ready`.
- Append a result line to `.exchange`, including the validator version used for fixture updates.
