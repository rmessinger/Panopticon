# Task Card `WI-A1-17`: Capture run identity

| Field | Value |
|---|---|
| Card ID | `WI-A1-17` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6c run identity |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [A1-6 §5.4](../../deep-dives/a1-6-operational-recording.md#54-run-identity) |

## Goal

Add `RunIdentity` so operational bundles can refuse reconstruction under a different build, runtime, platform, seed, shard layout, or input set.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §5.4: identity fields and mismatch rule.
- `src/Panopticon.Simulation/RunOutput.cs:62-110`: run manifest shape.
- `Directory.Packages.props`: package central management.
- `src/Panopticon.Simulation/Recording/RunRecording.cs:382-398`: run recording attachment inputs.

## Steps

1. Add `System.IO.Hashing` to `Directory.Packages.props` if absent.
2. Add `RunIdentity` with loaded `Panopticon.*` module version ids, runtime description, OS architecture, process architecture, SIMD flags, shard layout id, seed, and input hashes.
3. Hash scenario or map inputs, parameters, datasets, and the loaded game archive list with XxHash3.
4. Cache install archive content hashes outside the install directory by name, size, and last-write timestamp where a cache seam already exists; otherwise add a narrow cache under the run-output infrastructure.
5. Add `Matches` and a stable serialized hash for checkpoint headers and manifest comparison.
6. Add unit tests for identity equality, identity mismatch details, and stable hash output for fixed synthetic inputs.

## Files likely to change

- `Directory.Packages.props`
- `src/Panopticon.Simulation/Recording/RunIdentity.cs`
- `src/Panopticon.Simulation/RunOutput.cs` if manifest wiring is included
- `test/Panopticon.Simulation.Tests/Recording/RunIdentityTests.cs`

## Acceptance criteria

- [ ] `RunIdentity` records all §5.4 fields available on the current platform.
- [ ] Synthetic fixed inputs produce a stable identity hash.
- [ ] A seed, shard layout, archive hash, or runtime-field change produces a reported mismatch.
- [ ] `System.IO.Hashing` is present only once in central package management.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~RunIdentity"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not implement reconstructor identity refusal yet. Do not make `Full` run-output JSON change unless guarded by null manifest fields in a later card.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A1-21`.
- List follow-up cards to create here:
  - none

## Result

- `RunIdentity` (Recording/RunIdentity.cs) captures loaded `Panopticon.*` module version ids (Tests assemblies excluded, ordinal-sorted), runtime description, OS/process architecture, SIMD flags (`Vector512.IsHardwareAccelerated`, `Avx2.IsSupported`, `AdvSimd.IsSupported`), shard layout id, seed, input hashes, and archive hashes. `Capture(seed, shardLayoutId, inputs, archiveFilePaths, cache?)` is the production entry; an internal all-fields constructor serves tests.
- Inputs (scenario/map, parameters, datasets) hash via `XxHash3.HashToUInt64`; archives hash incrementally through an 80 KiB `Append` loop so multi-hundred-MB `.big` files never load into memory.
- `ArchiveContentHashCache` persists `name|size|lastWriteTicks -> hash` JSON under `LocalApplicationData/Panopticon/archive-hash-cache.json` (outside every install directory); cache misses recompute and save, corrupt cache files fall back to empty. `ArchiveContentHashCache.Shared` is the default seam; tests inject an explicit path.
- `Matches`/`Differences` report per-field mismatches including module version, input hash, archive length/hash, and missing-on-one-side entries; `StableHash` returns the XxHash3 hex string of a canonical length-prefixed byte serialization (checkpoint headers and manifests, WI-A1-21).
- `System.IO.Hashing` 10.0.0 added once to `Directory.Packages.props`; `Panopticon.Simulation.csproj` gained the versionless reference. Not wired into `RunOutput` or any refusal path (A1-21 scope).
- Tests: `RunIdentityTests` 11/11 (equality + stable hash, seed/shard/runtime/module/archive mismatch details, missing-side reporting, per-field hash sensitivity, `Capture` runtime/module fill, cache reuse + mtime-keyed recompute + persistence reload, `Capture` archive hashing through the cache).

