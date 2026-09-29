# Task Card `WI-A1-20`: Write the operational stream

| Field | Value |
|---|---|
| Card ID | `WI-A1-20` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6d operational stream |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-18`, `WI-A1-19`, `WI-A4-44` |
| Diagrams | [A1-6 §5.2](../../deep-dives/a1-6-operational-recording.md#52-operational-stream) |

## Goal

Add the `operational.bin` writer and reader with crash-resilient flushed chunks, rebuildable indexing, and A4-4 domain-state records.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §5.2 and §5.5: stream structure and hash records.
- `src/Panopticon.Simulation/Recording/RunRecording.cs:15-125` and `:382-398`: recording lifecycle.
- `src/Panopticon.Simulation/Recording/DomainStateRecorder.cs`: A4-4 artifact writer patterns.
- `src/Panopticon.Simulation/Recording/OperationalStateHasher.cs`: hashes from `WI-A1-18`.
- `src/Panopticon.Simulation/Simulation/IReconstructableDomain.cs`: cadence and rollup contract from `WI-A1-19`.

## Steps

1. Add `OperationalStreamWriter` and `OperationalStreamReader` with magic `PNOR`, format version, run id, domain id, and domain schema version.
2. Write one length-prefixed Brotli-compressed chunk per operational boundary and flush after each chunk.
3. Encode records `Tick`, `Strings`, `DomainDelta`, `DomainSnapshot`, `Rollups`, `Metrics`, `Events`, `Inputs`, `Hashes`, and optional `T1`.
4. Write `DomainSnapshot` at checkpoint boundaries only.
5. Use append-only string table ids for keys, kinds, series, and definition names.
6. Write a footer index and implement reader index rebuild by scanning when the footer is missing.
7. Add tests for complete stream round-trip, missing footer rebuild, truncated final chunk ignoring only the incomplete chunk, and unknown format rejection.

## Files likely to change

- `src/Panopticon.Simulation/Recording/OperationalStreamWriter.cs`
- `src/Panopticon.Simulation/Recording/OperationalStreamReader.cs`
- `src/Panopticon.Simulation/Recording/OperationalRecords.cs`
- `test/Panopticon.Simulation.Tests/Recording/OperationalStreamTests.cs`

## Acceptance criteria

- [ ] A reader loads all completed flushed chunks after a simulated crash before footer write.
- [ ] Footer and scan-built indexes return the same frame-to-offset mapping for complete streams.
- [ ] Unknown version and malformed records throw `InvalidDataException`.
- [ ] `Hashes` records match `OperationalStateHasher` outputs from the same boundary.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~OperationalStream"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not schedule engine checkpoints or write manifest fields yet. Do not implement reconstruction sinks.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A1-21`.
- List follow-up cards to create here:
  - none
