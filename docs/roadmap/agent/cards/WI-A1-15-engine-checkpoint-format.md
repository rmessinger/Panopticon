# Task Card `WI-A1-15`: Add the engine checkpoint format

| Field | Value |
|---|---|
| Card ID | `WI-A1-15` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6b engine checkpoint format |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-13`, `WI-A1-14`, `WI-A4-43` |
| Diagrams | [A1-6 §5.3](../../deep-dives/a1-6-operational-recording.md#53-engine-checkpoint) |

## Goal

Add the `PNCK` checkpoint writer and reader with sectioned compressed payloads for engine, random, host, domain runtime, and domain state.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §5.3 and §5.5: checkpoint format and hash inputs.
- `src/Panopticon.Simulation/Data/Sav/SaveFile.cs:53-175`: logic chunk persistence.
- `src/Panopticon.Simulation/StatePersister.cs:292-424`: binary state persistence.
- `src/Panopticon.Simulation/SimulationHost.cs:68-147`: host frame and tick flow.
- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §4-§5: domain-state frame persistence.

## Steps

1. Add `EngineCheckpoint` with header magic `PNCK`, format version, frame, theater time, identity hash, and run id.
2. Write length-prefixed Brotli sections named `engine`, `random`, `host`, `domain-runtime`, and `domain-state`.
3. Write `engine` from `SaveFile.Persist` logic chunks through `StateWriter`, using the presentation exclusion list from `WI-A1-13`.
4. Write `random` from the persistable `GameLogic.Random` state.
5. Write `host` frame, map time, and pending injected orders.
6. Write `domain-runtime` through `IReconstructableDomain.PersistRuntime` when available.
7. Write `domain-state` from the full A4-4 `DomainStateFrame`, all layers, when available.
8. Add format round-trip tests that reject bad magic, unknown required sections, and truncated compressed payloads.

## Files likely to change

- `src/Panopticon.Simulation/Recording/EngineCheckpoint.cs`
- `src/Panopticon.Simulation/Recording/CheckpointSection.cs` if needed
- `test/Panopticon.Simulation.Tests/Recording/EngineCheckpointFormatTests.cs`

## Acceptance criteria

- [ ] A checkpoint file contains the five named sections when the domain supports runtime and state projection.
- [ ] A domain without projection omits or records an empty `domain-state` section by documented rule.
- [ ] Reader validation rejects bad magic, unsupported version, truncated Brotli data, and duplicate required sections.
- [ ] Presentation chunks are not written to the `engine` section.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~EngineCheckpointFormat"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not implement host restore from the checkpoint. Do not wire checkpoint scheduling into `RunRecording` yet.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A1-16` and `WI-A1-21`.
- List follow-up cards to create here:
  - none

## Result

Implemented in `src/Panopticon.Simulation/Recording/EngineCheckpoint.cs` with tests in
`test/Panopticon.Simulation.Tests/Recording/EngineCheckpointFormatTests.cs`.

- PNCK container: magic + format version 1 + frame + theater-time ticks + identity hash + run id,
  then length-prefixed Brotli-compressed named sections.
- `CaptureSections(SimulationHost, identityHash, runId)` always writes `engine` (SaveFile logic
  chunks only, presentation excluded), `random` (SageRandom seed + six state words), and `host`
  (frame, map time, pending injected orders). `domain-runtime` is written when the domain model is
  `IReconstructableDomain`; `domain-state` when it is `IDomainStateProjection`; a domain without
  projection simply omits both by that rule.
- Order arguments are encoded per argument type (Integer, Float, Boolean, ObjectId, Position,
  ScreenPosition, Rectangle) and rebuilt through `Order.Add*Argument`.
- Reader rejects bad magic, unsupported version, duplicate sections, missing required sections,
  truncated Brotli payloads, corrupt Brotli data, and uncompressed-length mismatches.
- Validation: EngineCheckpointFormatTests 12/12 with `CNC_GENERALS_PATH` (11 without env: the
  installed-game test is a GameFact); whole-solution `dotnet format --verify-no-changes` clean;
  `check_brace_newline.py` and `git diff --check` clean.

## Revision: storage-first A1-6 (#147)

- Header drops the identity hash: magic, format version, frame, theater time, run id.
- `CaptureSections(SimulationHost)` takes no identity or run id.
- `EngineCheckpoint.ContentHash` computes the manifest XxHash3 for checkpoint files.
