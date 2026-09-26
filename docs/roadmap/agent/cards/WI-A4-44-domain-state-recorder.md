# Task Card `WI-A4-44`: Record domain state through the run recording

| Field | Value |
|---|---|
| Card ID | `WI-A4-44` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-4 domain state projection |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-43` |
| Diagrams | [A4-4 §8](../../deep-dives/a4-4-domain-state-projection.md#8-architecture-diagrams) |

## Goal

Add `DomainStateRecorder` and its artifact, wire it into `RunRecording` when the domain implements
`IDomainStateProjection`, and expose it in `RunOutput` without changing RTS output.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §5.
- `src/Panopticon.Simulation/Recording/RunRecording.cs`: `WorldStateSampler`,
  `SnapshotDeltaRecorder`, `RunRecording.Attach`/`Complete`.
- `src/Panopticon.Simulation/Recording/WorldStateSnapshot.cs`: `WorldStateArtifactWriter` and `Load`.
- `src/Panopticon.Simulation/RunOutput.cs:62-100`.

## Steps

1. Add `src/Panopticon.Simulation/Recording/DomainStateRecorder.cs`: subscribes to
   `WorldStateSampler.FrameSampled`, validates the schema on construction, keeps snapshots and
   deltas, exposes `ReconstructStateAt`.
2. Add `DomainStateArtifactWriter` and `DomainStateArtifact.Load` with tags 1 schema header,
   2 snapshot, 3 delta; reject unknown tags and versions with `InvalidDataException`.
3. Extend `RunRecording` with a nullable `DomainState` property and an optional
   `domainStateArtifactPath` parameter on `Attach`; create the recorder only when
   `host.DomainModel is IDomainStateProjection`; dispose it.
4. Add nullable `DomainStateSchemaVersion`, `DomainStateArtifactPath`, `DomainStateSnapshotFrames` to
   `RunOutput` with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`; fill them in
   `Complete` only when the recorder exists.
5. Tests with a small in-test domain wrapping `TransportGraphDomainModel` plus a trivial projection:
   artifact load equals in-memory frames; reconstruction matches live capture; an RTS-style domain
   without the projection yields JSON with none of the new property names.

## Files likely to change

- `src/Panopticon.Simulation/Recording/DomainStateRecorder.cs`
- `src/Panopticon.Simulation/Recording/RunRecording.cs`
- `src/Panopticon.Simulation/RunOutput.cs`
- `test/Panopticon.Simulation.Tests/Simulation/DomainStateRecorderTests.cs`

## Acceptance criteria

- [ ] A projection domain run writes an artifact whose loaded frames equal the recorder's in-memory frames.
- [ ] `ReconstructStateAt(f)` equals the live `CaptureState(f)` at every sampled `f`.
- [ ] Run-output JSON of a domain without the projection contains no `domainState` properties.
- [ ] Existing A1/A4 recording tests pass unmodified.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~DomainStateRecorder|FullyQualifiedName~SimulationDomainContract|FullyQualifiedName~RunRecording"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No T1 signature integration; no change to `WorldObjectState` or its artifact.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-45`, `WI-W0-07`, and `WI-W0-09`.
