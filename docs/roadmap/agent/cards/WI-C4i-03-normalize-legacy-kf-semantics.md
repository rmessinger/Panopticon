# Task Card `WI-C4i-03`: Normalize legacy KF semantics

> Audience: agent task card.

| Field | Value |
|---|---|
| Card ID | `WI-C4i-03` |
| Roadmap phase | [Track C · C4i](../../track-c-external-assets.md): EE2 legacy KF formats |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4i-02`, `WI-C4c-04` |
| Diagrams | [C4i architecture diagrams](../../deep-dives/c4i-ee2-legacy-kf-formats.md#10-architecture-diagrams) |

## Goal

Feed accepted 10.0.1.0 keyframe data into the C4c converter while handling legacy text-key
spelling, uppercase paths, and proof-clip controller flags.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4i-ee2-legacy-kf-formats.md` sections 2, 3.2, 5, and 8.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 4, 6, and 7.
- C4c `NifAnimationImporter`, `NifAnimationConverter`, `NifKeyCurve`, and `NifRotationSampler`
  files added by `WI-C4c-03` and `WI-C4c-04`.
- `src/OpenSage.IO` ZIP file-system lookup behavior used by C4b texture resolution.

## Steps

1. Add a small legacy-version policy used by the C4c converter. It trims text-key marker values
   before comparing `start` and `end`.
2. Accept legacy proof flags `0x05` and `0x0C` for NIF 10.0.1.0 after frequency, phase, target
   ref, next ref, data ref, and timing validation pass.
3. Keep exact target-name matching. Do not trim or normalize controlled-block target names.
4. Ensure `#kf=` path resolution is case-insensitive for `.kf` and `.KF` archive entries.
5. Add synthetic converter tests for `end `, flags `0x05` and `0x0C`, uppercase `.KF` lookup, and
   a rejected unknown legacy flag.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/Nif/NifAnimationImporter.cs`
- `src/OpenSage.Game/Content/Importing/Nif/NifAnimationConverter.cs`
- `src/OpenSage.Game/Content/Importing/Nif/NifLegacyVersionPolicy.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/NifAnimationImporterTests.cs`
- `src/OpenSage.Game.Tests/Content/Importing/NifAnimationConverterTests.cs`

## Acceptance criteria

- [ ] Legacy `end ` text keys import as `end` without changing target names.
- [ ] Legacy proof flags `0x05` and `0x0C` import, and an unapproved flag fails.
- [ ] Uppercase `.KF` animation keys resolve through the same importer path as lowercase `.kf`.
- [ ] C4c 10.1 proof clips keep their existing behavior.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifAnimation"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not evaluate unsupported material, visibility, UV, or morph controllers.
- Do not trim controlled-block target names.
- Do not loosen C4c scale, accumulation-root, or key-type failures.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4i-04` to `Ready` when `WI-C4c-05` is done.
- Append a result line to `.exchange`.
