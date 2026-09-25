# Task Card `WI-A5-08`: Add an indexed bounded-memory run-state reader

| Field | Value |
|---|---|
| Card ID | `WI-A5-08` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-3 and A5-5 replay foundation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-07` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Seek and reconstruct frames without loading every snapshot and delta into memory.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a5-scenario-visualization.md`: reader contract.
- `src/Panopticon.Simulation/Recording/WorldStateSnapshot.cs`.
- `src/Panopticon.Simulation/StatePersister/StateReader.cs`.
- Existing tiered-recording tests.

## Steps

1. Add a reader that indexes record offsets and metadata only.
2. Reconstruct a requested frame from the nearest prior snapshot and required deltas.
3. Add a configurable bounded frame/state cache.
4. Expose valid-prefix information for truncated artifacts.
5. Add forward, backward, frame-zero, intermediate, long-file, and truncation tests.

## Files likely to change

- New or updated files under `src/Panopticon.Simulation/Recording/`
- New cursor model under `src/Panopticon.Tools.ReplaySketch.Core/`
- Focused Game and ReplaySketch tests

## Acceptance criteria

- [ ] Exact snapshot and intermediate frames reconstruct identically in both seek directions.
- [ ] Resident decoded records are bounded independently of total run length.
- [ ] Truncation returns an explicit valid prefix and diagnostic.
- [ ] No simulation rerun is used for reconstruction.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WorldStateArtifactReader|FullyQualifiedName~RunStateCursor"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No rendering, culling, or file-format redesign beyond the indexed reader.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
