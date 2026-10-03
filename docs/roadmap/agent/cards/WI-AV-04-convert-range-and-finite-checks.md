# Task Card `WI-AV-04`: Convert range and finite checks, update tests

| Field | Value |
|---|---|
| Card ID | `WI-AV-04` |
| Plan | [Single-line argument validation](../../plans/argument-validation.md) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-AV-01` |
| Diagrams | N/A: mechanical refactor |

## Goal

Convert simple range and finite guards to `Ensure` (or BCL `ArgumentOutOfRangeException.ThrowIf*`
where no message exists), switching their exception type to `ArgumentOutOfRangeException` and
updating the tests that assert the old type.

## Context to read (and only this)

- `docs/roadmap/plans/argument-validation.md`: `Decisions` and `Ensure API`.
- `src/Panopticon.Core/Ensure.cs`.

## Sites

| File | Line (2026-10-03) | Condition | Target |
|---|---|---|---|
| `src/Panopticon.Simulation/Content/Importing/ImportedMesh.cs` | 81 | `boundingSphere.Radius < 0` | `Ensure.AtLeast(..., 0f, msg)` |
| `src/Panopticon.Simulation/Content/Importing/ImportedKeyframe.cs` | 35 | `time < TimeSpan.Zero` | `Ensure.AtLeast(time, TimeSpan.Zero, msg)` |
| `src/Panopticon.Simulation/Content/Importing/ImportedAnimation.cs` | 30 | `duration < TimeSpan.Zero` | `Ensure.AtLeast` |
| `src/Panopticon.Simulation/Content/Importing/ImportedMeshPart.cs` | 46 | `materialIndex < 0` | `Ensure.AtLeast` |
| `src/Panopticon.Simulation/Content/Importing/ImportedAnimationTarget.cs` | 42 | `directIndex < 0` | `Ensure.AtLeast` |
| `src/Panopticon.Simulation/Content/Importing/ImportedSubObject.cs` | 35 | `parentBoneIndex < 0` | `Ensure.AtLeast` |
| `src/Panopticon.Simulation/Content/Importing/ImportedBone.cs` | 40 | `parentIndex < -1` | `Ensure.AtLeast(parentIndex, -1, msg)` |
| `src/Panopticon.Simulation/Content/Importing/Gltf/GltfImportOptions.cs` | 67 | non-finite or `unitsPerMeter <= 0` | `Ensure.FinitePositive` |
| `src/Panopticon.Simulation/Content/Importing/Gltf/GltfImportOptions.cs` | 71, 76, 81, 86, 107 | `<= 0` | `Ensure.GreaterThan(x, 0L, msg)` |
| `src/Panopticon.Simulation/Content/Importing/Nif/NifKeyCurve.cs` | 25 | `last < 0` | `Ensure.AtLeast` |
| `src/Panopticon.Simulation/Content/Importing/ImportedPbrMetallicRoughness.cs` | 71 | `alphaCutoff is < 0 or > 1` | `Ensure.InRange(alphaCutoff, 0f, 1f, msg)` |
| `src/Panopticon.Simulation/Content/Importing/ImportedTextureBinding.cs` | 54 | `uvSet is < 0 or > 1` | `Ensure.InRange` |
| `src/Panopticon.Simulation/Content/Importing/ImportedVertex.cs` | 48, 52 | `boneWeight < 0 or > 1` | `Ensure.InRange` |
| `src/Panopticon.Simulation/Content/Importing/ImportedFixedFunctionShading.cs` | 80 | `< 0 or > 2` | `Ensure.InRange` |
| `src/Panopticon.Simulation/RunOutput.cs` | 227 | `sampleInterval == 0` | BCL `ThrowIfZero` if no message |
| `src/Panopticon.Simulation/Recording/RunRecording.cs` | 81 | `snapshotInterval == 0` | BCL `ThrowIfZero` if no message |
| `src/Panopticon.Simulation/Logic/GameLogic.cs` | 350 | `index < 0 or >= Count` | `Ensure.InRange` or BCL pair |
| `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelImportOptions.cs` | 37 | non-finite or `<= 0` | `Ensure.FinitePositive` |
| `src/Panopticon.Mathematics/BitArray512.cs` | 70, 98 | `bit < 0 or >= Length` | BCL `ThrowIfNegative` + `ThrowIfGreaterThanOrEqual` |
| `src/Panopticon.Mathematics/MathUtility.cs` | 64 | `float.IsNaN(angle)` | brace-less `if` (no Core reference) |

Line numbers drift; locate each by its condition.

## Steps

1. Convert each site per the table, keeping the original message verbatim. The `ImportedBox.cs`
   vector-component check stays with `WI-AV-05`.
2. Find tests that exercise each converted guard:
   ```sh
   grep -rn --include='*.cs' 'Assert.Throws<ArgumentException>' test
   ```
   For each test whose invalid input hits a converted site, change the assertion to
   `Assert.Throws<ArgumentOutOfRangeException>`.
3. Run the tests and fix any remaining exact-type mismatches the same way.

## Files likely to change

- Files in the table.
- Matching tests under `test/Panopticon.Simulation.Tests`, `test/Panopticon.Mathematics.Tests`,
  `test/Panopticon.Tools.ReplaySketch.Tests`.

## Acceptance criteria

- [ ] Every table site is single-line and throws `ArgumentOutOfRangeException`.
- [ ] Every converted message is byte-identical to the original.
- [ ] No test uses `Assert.ThrowsAny` to paper over a type change.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- `ImportedAnimationChannel.cs` `Enum.IsDefined` check and other compound conditions: `WI-AV-05`.

## On completion

- Set Status to `Done`.
- Append a result line to repo-root `.exchange`.
