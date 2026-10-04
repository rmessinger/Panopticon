# Task Card `WI-A1-18`: Hash operational state

| Field | Value |
|---|---|
| Card ID | `WI-A1-18` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6c operational state hash |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-13` |
| Diagrams | [A1-6 §5.5](../../deep-dives/a1-6-operational-recording.md#55-state-hash) |

## Goal

Add `OperationalStateHasher` that hashes checkpoint-equivalent bytes into deterministic world, bucket, and globals hashes at operational boundaries.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §5.5: bucket and globals hashing contract.
- `src/Panopticon.Simulation/Recording/WorldStateSnapshot.cs:25-60`: per-object persisted state shape.
- `src/Panopticon.Simulation/Recording/SignatureTree.cs:66-73`: T1 level-1 bucket keys.
- `src/Panopticon.Simulation/StatePersister.cs:292-424`: `StateWriter` bytes.
- `src/Panopticon.Simulation/Simulation/State/IDomainStateProjection.cs`: domain-state capture when present.

## Steps

1. Add `OperationalStateHasher` and `OperationalStateHashes` records.
2. Persist each object through `StateWriter` into an XxHash3 hashing stream.
3. Assign object hashes to the bucket of the object T1 level-1 key, using spatial cell keys for RTS and `region:` keys for Western Front where available.
4. Fold non-object chunks, random state, host state, and domain runtime state into a globals hash.
5. Combine bucket hashes in ordinal key order into the world hash.
6. Add tests proving object-order independence inside buckets, bucket-order dependence only by ordinal key, and globals changes on random or host-state changes.

## Files likely to change

- `src/Panopticon.Simulation/Recording/OperationalStateHasher.cs`
- `src/Panopticon.Simulation/Recording/OperationalStateHashes.cs`
- `test/Panopticon.Simulation.Tests/Recording/OperationalStateHasherTests.cs`

## Acceptance criteria

- [ ] Equal checkpoint-equivalent bytes produce equal world, bucket, and globals hashes.
- [ ] A changed object changes only its bucket and the world hash.
- [ ] A changed random or host value changes the globals and world hash.
- [ ] World hash composition sorts bucket keys ordinally.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~OperationalStateHasher"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not write `Hashes` records to `operational.bin` yet. Do not add probabilistic or tolerance-based hashes.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A1-20`.
- List follow-up cards to create here:
  - none

## Result

Implemented `OperationalStateHasher.HashPersisted` (per-object `StateWriter` bytes into XxHash3)
and `Build(bucketObjectHashes, globalsSections)` producing `OperationalStateHashes`
(WorldHash/GlobalsHash/Buckets). Bucket folds and the globals fold are commutative
(object/section order independent, mirroring `SignatureTree`'s Mix/FNV approach); the world hash
seeds from the globals hash and folds bucket hashes in ordinal bucket-key order. Tests
(6): equal inputs equal at every level; object order within a bucket irrelevant; bucket supply
order irrelevant; changed object alters only its bucket plus world; changed random/host section
alters only globals plus world; `HashPersisted` distinguishes changed checkpoint bytes.
Hashes are not yet written to the operational stream (WI-A1-20 scope).
