# Task Card `WI-C4i-01`: Add legacy KF header layouts

> Audience: agent task card.

| Field | Value |
|---|---|
| Card ID | `WI-C4i-01` |
| Roadmap phase | [Track C · C4i](../../track-c-external-assets.md): EE2 legacy KF formats |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4c-01` |
| Diagrams | [C4i architecture diagrams](../../deep-dives/c4i-ee2-legacy-kf-formats.md#10-architecture-diagrams) |

## Goal

Extend NIF header parsing to distinguish C4c 10.1.0.0, legacy 10.0.1.0, and rejected
NetImmerse 4.2.2.0 KF files with exact footer consumption.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4i-ee2-legacy-kf-formats.md` sections 1, 3, 4.1, 4.3, and 8.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 3 and 7.
- `src/Panopticon.FileFormats.Nif/NifHeader.cs`.
- `src/Panopticon.FileFormats.Nif/NifFile.cs:21-56`.
- `src/Panopticon.FileFormats.Nif/NifBinaryReader.cs`.
- `test/Panopticon.Simulation.Tests/Data/Nif/NifTestDataBuilder.cs`.

## Steps

1. Replace the single-version constants in `NifHeader` with a small version layout value that
   records header string, version, optional user version, block-table availability, group-table
   availability, and whether block bodies have leading words.
2. Parse `0x0A010000` as the existing 10.1.0.0 layout with user version `0`.
3. Parse `0x0A000100` as a NetImmerse 10.0.1.0 layout with no user-version field, then read num
   blocks, block types, block type indices, and zero groups.
4. Reject `0x04020200` with `NotSupportedException` naming NetImmerse 4.2.2.0 and the missing
   block directory.
5. Change the footer reader in `NifFile.Read` to read `num roots` plus root references. Keep the
   existing C4a and C4c behavior for files whose root list is `[0]`.
6. Add synthetic tests for 10.1.0.0, 10.0.1.0 with `NumBlocks` immediately after version, a
   10.0.1.0 two-root footer, and 4.2.2.0 rejection.

## Files likely to change

- `src/Panopticon.FileFormats.Nif/NifHeader.cs`
- `src/Panopticon.FileFormats.Nif/NifFile.cs`
- `test/Panopticon.Simulation.Tests/Data/Nif/NifTestDataBuilder.cs`
- `test/Panopticon.Simulation.Tests/Data/Nif/NifHeaderTests.cs`

## Acceptance criteria

- [ ] 10.1.0.0 files still parse with user version `0` and the same block directory.
- [ ] 10.0.1.0 files parse `NumBlocks` as the first post-version word, not as user version.
- [ ] Root-list footers with one or two roots consume exactly to end-of-stream.
- [ ] NetImmerse 4.2.2.0 fails before block dispatch with a stable message.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifHeaderTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add animation conversion or curve evaluation.
- Do not implement the 4.2.2.0 inline block-type container.
- Do not embed or copy EE2 asset bytes.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4i-02` to `Ready` when `WI-C4c-01` is done.
- Append a result line to `.exchange`.
