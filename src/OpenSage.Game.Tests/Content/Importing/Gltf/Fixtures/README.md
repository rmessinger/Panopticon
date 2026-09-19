# glTF Fixture Matrix

CPU-only fixtures for the C4 glTF importer. Every file is project-authored; no third-party
model assets. There are no test-time downloads and no network access.

## Layout

Generator-produced (deterministic `GltfFixtureGenerator` output; regeneration is
byte-stable):

- `external-triangle.gltf` + `.bin` - static triangle with an external buffer.
- `data-uri-triangle.gltf` (+ `.bin` sibling ignored) - same triangle with a merged
  data-URI buffer.
- `static-triangle.glb` - same triangle in a GLB container.
- `rigid-scene.gltf` + `.bin` - named rigid node with translation (LINEAR) and rotation
  (STEP) animation and a one-meter rig translation.
- `pbr-materials.gltf` + `.bin` (+ satellite images) - PBR material with base-color and
  emissive textures, MASK alpha, cutoff, double-sided state, UV0 and UV1.
- `two-influence-skin.gltf` + `.bin` - two-influence skin with reordered named joints,
  non-identity rest transforms, and non-identity inverse binds.
- `animated-skin.glb` - the skinned model with a LINEAR rotation and STEP translation
  channel.
- `base-color.png`, `emissive.jpg` - deterministic ImageSharp-encoded fixture images
  (4x4 checker, fixed encoder settings).

Hand-authored (static by construction, `GltfHandAuthoredFixtures`):

- `accessor-layouts.*` - one document with interleaved POSITION+NORMAL (stride 24), a
  sparse POSITION overlay, and unsigned-int indices.
- `unsupported-morph.gltf` + `.bin` - morph target; importer rejects with
  `NotSupportedException`.
- `unsupported-mirrored-node.gltf` - negative-determinant node; `NotSupportedException`.
- `unsupported-required-extension.gltf` - required extension; strict parse fails with
  `InvalidDataException`.
- `malformed-accessor.gltf` + `.bin` - out-of-range index; `InvalidDataException`.
- `unsupported-cubicspline.gltf` + `.bin` - CUBICSPLINE sampler; `NotSupportedException`.
- `unsupported-third-influence.gltf` + `.bin` - meaningful third influence;
  `NotSupportedException`.
- `unsupported-multiple-skins.gltf` - two skins; `NotSupportedException`.

## Regenerating

Set `OPENAGE_GLTF_FIXTURE_OUTPUT` to this directory and run the generator tests; both
generation paths (generator and hand-authored) rewrite every file in place:

```sh
OPENAGE_GLTF_FIXTURE_OUTPUT="$PWD/src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures" \
  dotnet test src/OpenSage.Game.Tests --filter "FullyQualifiedName~GltfFixtureGeneratorTests"
```

## Fixture updates and the Khronos validator

After regenerating, validate changed `.gltf`/`.glb` files with a locally installed official
Khronos glTF Validator (`gltf-validator`, `npx gltf-validator`, or the web validator applied
manually). Record the validator version here with each update:

- 2026-09-19: initial fixture set; validator not yet run (first install pending).

Tests never invoke or download the validator.
