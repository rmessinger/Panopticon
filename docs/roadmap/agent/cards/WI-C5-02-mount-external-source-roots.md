# Task Card `WI-C5-02`: Mount external source roots

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C5-02` |
| Roadmap phase | [Track C · C5](../../track-c-external-assets.md): Authoring and validation tooling |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4-02`, `WI-C5-01` |
| Diagrams | [C5 architecture diagrams](../../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) |

## Goal

Create disposable read-only external source mounts that expose normalized virtual keys and keep
model and animation roots isolated from each other and from game assets.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c5-external-model-tooling.md` sections 3, 5, and 8.
- `docs/roadmap/deep-dives/c4-gltf-importer.md` section 3.
- `src/Panopticon.IO/FileSystem.cs`.
- `src/Panopticon.IO/FileSystemEntry.cs`.
- `src/Panopticon.IO/DiskFileSystem.cs`: disk delegation and case-insensitive lookup.
- `src/Panopticon.IO/VirtualFileSystem.cs`: existing prefix wrapper and its traversal limitations.
- `src/Panopticon.Simulation/Content/Importing/AssetImportContext.cs`.
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfResourceResolver.cs` (new, from `WI-C4-02`).
- `src/Panopticon.Simulation/Content/Importing/ExternalFormatSelection.cs` (new, from `WI-C5-01`).

## Steps

1. Add a public sealed read-only filesystem wrapper over one `DiskFileSystem` root.
2. Before delegation, reject empty, rooted, escaping, repeated-separator, dot-segment,
   alternate-separator, and otherwise non-normalized file or directory keys.
3. Verify the combined full path remains under the canonical root using the platform-appropriate
   path comparison. Never pass a rejected key to `DiskFileSystem`.
4. Add public sealed disposable `ExternalAssetSourceMount` construction from an absolute selected
   file. Store the canonical source root, selected filename as the root virtual key, format
   selection, and one `AssetImportContext`.
5. Support independent model and animation mount instances. Do not combine them with
   `CompositeFileSystem` or the game installation filesystem.
6. Ensure disposal closes the delegated filesystem and is idempotent.
7. Add tests for valid nested relatives, enumeration, each rejected path form, symlink escape where
   supported, case behavior, independent roots, and disposal.

## Files likely to change

- `src/Panopticon.IO/ExternalSourceFileSystem.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/ExternalAssetSourceMount.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/ExternalSourceFileSystemTests.cs` (new)

## Acceptance criteria

- [ ] Only normalized relative virtual keys reach the delegated `DiskFileSystem`.
- [ ] Rooted, escaping, dot-segment, separator-alias, and symlink-escaping paths are rejected.
- [ ] Model and animation mounts can point at different roots without seeing each other's files.
- [ ] Neither external root is layered into the host game filesystem.
- [ ] Both mount types are public; ReplaySketch Core needs no friend-assembly access.
- [ ] Mount disposal is explicit, idempotent, and covered by tests.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExternalSourceFileSystemTests|FullyQualifiedName~GltfResourceResolverTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not relax C4 URI or virtual-key validation.
- Do not add write, delete, create-directory, or source-rewrite operations.
- Do not use `CompositeFileSystem`, ambient current-directory lookup, or the game asset store.
- Do not retain mounts after their owning session or host request is disposed.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C5-03` is done, change `WI-C5-04` to `Ready`.
- Append a result line to `.exchange`.
