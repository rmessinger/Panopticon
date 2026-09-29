# Task Card `WI-A1-17`: Capture run identity

| Field | Value |
|---|---|
| Card ID | `WI-A1-17` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6c run identity |
| Status | `Ready` |
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
