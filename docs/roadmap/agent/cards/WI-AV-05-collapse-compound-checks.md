# Task Card `WI-AV-05`: Collapse compound and unique checks

| Field | Value |
|---|---|
| Card ID | `WI-AV-05` |
| Plan | [Single-line argument validation](../../plans/argument-validation.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-AV-01` |
| Diagrams | N/A: mechanical refactor |

## Goal

Convert the remaining braced `Argument*Exception` guards: empty-collection checks to
`Ensure.NotEmpty`, everything else to a brace-less two-line `if`.

## Context to read (and only this)

- `docs/roadmap/plans/argument-validation.md`: `Ensure API` selection rule.
- `docs/coding-style.md` rule 1 (brace-less single statements).

## Steps

1. List remaining sites (run after `WI-AV-02`..`04` land, or skip sites they own). A site is braced when the line above the `throw` is `{`:
   ```sh
   grep -rn --include='*.cs' -B2 'throw new Argument[A-Za-z]*Exception' src test
   ```
2. `x.Count == 0` / `x.Length == 0` with a message: `Ensure.NotEmpty(x, "<message>");`. Known
   sites: `ImportedMesh.cs` (vertices, indices, materials, meshParts), `ImportedSkeleton.cs`
   (bones), `ImportedAnimationChannel.cs` (keyframes), `GltfSkinConverter.cs` (SkinnedNodes),
   `ImportedImageSource.cs` (`bytes is null || bytes.Length == 0`).
3. All other conditions (`% 3 != 0`, length mismatches, `Enum.IsDefined`, `TryGetValue`
   lookups, cross-argument checks in `RunRecording.cs`, `ImportedBox.cs` vector components,
   `BinaryWriterExtensions.cs` length limits, `SoftFloat.CompareTo`) become:
   ```csharp
   if (indices.Count % 3 != 0)
       throw new ArgumentException("Index count must be a multiple of 3.", nameof(indices));
   ```
4. Do not apply step 3 inside a block nested in another braced statement block (rule 1); keep
   braces there.
5. `Veldrid.ImageSharp`, `Panopticon.Mathematics`, `Panopticon.FileFormats`: brace-less `if` only.

## Files likely to change

- Files returned by step 1.

## Acceptance criteria

- [ ] Step 1 returns only guards that rule 1 requires to keep braces.
- [ ] Exception types and messages unchanged, except `Ensure.NotEmpty` throwing
      `ArgumentNullException` for a previously combined null-or-empty condition; update the
      matching test if one asserts `ArgumentException`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No logic changes to the conditions themselves.
- No new `Ensure` methods; escalate if a recurring pattern needs one.

## On completion

- Set Status to `Done`.
- Append a result line to repo-root `.exchange`.
