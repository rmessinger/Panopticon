# Task Card `WI-A1-23`: Cache reconstructed checkpoints

| Field | Value |
|---|---|
| Card ID | `WI-A1-23` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6f reconstruction cache |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-22` |
| Diagrams | [A1-6 §6.3](../../deep-dives/a1-6-operational-recording.md#63-reconstruction-cache) |

## Goal

Add an LRU cache of derived checkpoints at operational boundaries so repeated scrubs re-simulate at most one operational interval.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §6.3: cache key, lifetime, and identity rules.
- `src/Panopticon.Simulation/Recording/RunReconstructor.cs`: reconstruction loop from `WI-A1-22`.
- `src/Panopticon.Simulation/Recording/EngineCheckpoint.cs`: checkpoint write and restore helpers.
- `src/Panopticon.Simulation/Recording/OperationalStateHashes.cs`: recorded boundary hash carried by derived checkpoints.

## Steps

1. Add `ReconstructionCache` keyed by run id and frame, with a configurable byte budget and LRU eviction.
2. Store derived checkpoints only at operational boundaries after their recorded hash matches.
3. Discard cache entries when run identity changes or the stored boundary hash differs from the bundle.
4. Teach `RunReconstructor` to prefer the nearest cached checkpoint at or before `fromFrame` before falling back to bundle checkpoints.
5. Add tests for cache hit selection, LRU eviction, identity mismatch discard, and boundary-hash mismatch discard.

## Files likely to change

- `src/Panopticon.Simulation/Recording/ReconstructionCache.cs`
- `src/Panopticon.Simulation/Recording/RunReconstructor.cs`
- `test/Panopticon.Simulation.Tests/Recording/ReconstructionCacheTests.cs`

## Acceptance criteria

- [ ] A second reconstruction inside a previously reconstructed hour starts from a cached boundary checkpoint.
- [ ] LRU eviction keeps total stored bytes at or under the configured budget.
- [ ] Identity and boundary-hash mismatches discard entries before use.
- [ ] No cache file is written inside the immutable bundle directory.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ReconstructionCache"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not change bundle file formats. Do not use cache entries without rechecking identity and recorded boundary hash.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A1-24`.
- List follow-up cards to create here:
  - none

## Result

`ReconstructionCache` is an in-memory LRU keyed by (run id, frame) under a byte budget; nothing
is written inside the immutable bundle directory. `RunReconstructor.Open` accepts an optional
cache, `Reconstruct` prefers the nearest valid cached checkpoint over the nearest bundle
checkpoint, and the step loop stores a derived checkpoint (via `EngineCheckpoint.CaptureSections`)
at every boundary whose hash just verified, except the checkpoint it started from. Validation on
use: a stored entry under a different identity purges that run's entries, and an entry whose
frame is missing from the bundle's recorded hashes, or whose stored hash differs, is discarded
before use.

One delivery gap surfaced by the cache-hit test and fixed here: restoring from a checkpoint at
or after `fromFrame` now delivers that boundary itself (state captured from the restored host,
verified against the recorded hash) before stepping; previously such a boundary was skipped.
Bundle-path reconstruction is unchanged (frame zero is not a recorded boundary).

Validation: 6/6 `ReconstructionCacheTests` (nearest selection, LRU eviction within budget,
run-scoped identity purge, boundary-hash discard incl. missing frames, oversized entry rejected,
installed cache-hit run that hides the bundle checkpoints and still reconstructs [20..45] from
the cached frame-20 checkpoint); `RunReconstructorTests` 6/6 unchanged; recording suites 69/69
with env; format/brace/diff clean.

## Revision: storage-first A1-6 (#147)

- The cache is keyed by run id and frame only (no identity or recorded hash) and stays in memory.
- A cached derived checkpoint serves later scrubs with the first reconstruction's state.
