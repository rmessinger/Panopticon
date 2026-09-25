# Task Card `WI-C4d-05`: Accept four glTF influences

| Field | Value |
|---|---|
| Card ID | `WI-C4d-05` |
| Roadmap phase | [Track C · C4d](../../track-c-external-assets.md): EE2 skinned character animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4d-03`, `WI-C4d-04` |
| Diagrams | [C4d architecture diagrams](../../deep-dives/c4d-ee2-skinned-animation.md#12-architecture-diagrams) |

## Goal

Import glTF `JOINTS_0` / `WEIGHTS_0` with up to four meaningful influences under the C4d
influence policy instead of rejecting the third and fourth.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4d-ee2-skinned-animation.md` section 4.
- `src/OpenSage.Game/Content/Importing/Gltf/GltfSkinConverter.cs:123-258`.
- `src/OpenSage.Game.Tests/Content/Importing/GltfSkinConverterTests.cs`.
- `docs/roadmap/deep-dives/c4-gltf-importer.md` scope list.

## Steps

1. In `DecodeInfluences`, keep every meaningful slot instead of throwing for slots 2 and 3.
2. Keep the weight validity and sum checks. Normalize by the sum and sort influences by
   descending weight.
3. Return four indices and four weights and pass them to `ImportedVertex`.
4. Replace the test that expects `NotSupportedException` for a third influence with tests for
   three and four influences, descending order, normalization, and dropping weights at or below
   1e-4.
5. Keep the test that rejects a bad weight sum.
6. In `c4-gltf-importer.md`, add one line under the scope list noting that C4d widens skinning
   to four influences, linking the C4d influence policy.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/Gltf/GltfSkinConverter.cs`
- `src/OpenSage.Game.Tests/Content/Importing/GltfSkinConverterTests.cs`
- `docs/roadmap/deep-dives/c4-gltf-importer.md`

## Acceptance criteria

- [ ] Three- and four-influence vertices import in descending weight order with normalized
      weights.
- [ ] Bad sums still throw `InvalidDataException`.
- [ ] Existing two-influence glTF fixtures produce identical vertices.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Gltf"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add `JOINTS_1` / `WEIGHTS_1` support.
- Do not change the NIF converter.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to `.exchange`.
