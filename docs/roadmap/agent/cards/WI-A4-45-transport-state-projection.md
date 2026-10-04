# Task Card `WI-A4-45`: Implement the state projection on the transport-graph domain

| Field | Value |
|---|---|
| Card ID | `WI-A4-45` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-4 domain state projection |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-44` |
| Diagrams | [A4-4 §8](../../deep-dives/a4-4-domain-state-projection.md#8-architecture-diagrams) |

## Goal

Prove A4-4 is domain-general by implementing `IDomainStateProjection` on
`TransportGraphDomainModel` and checking vehicle conservation over a full run.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §6.
- `src/Panopticon.Simulation/Simulation/Transport/TransportGraphDomainModel.cs`.
- `test/Panopticon.Simulation.Tests/Simulation/SimulationDomainContractTests.cs`: host fixture.

## Steps

1. Implement `StateSchema` (domain id `transport-graph`, version 1, kinds `node`, `edge`, `vehicle`
   per §6) and `CaptureState(frame)` with keys `node:{i}`, `edge:{from}->{to}`, `vehicle:{id}`.
2. Add a test that runs the default scenario to completion with `RunRecording.Attach(...)`, then
   asserts `DomainConservation.Residuals` is empty against the first frame at every sampled frame
   and that final `edge` throughput records equal `EdgeThroughput`.
3. Mark A4-4 done in the checklist, track doc, and README progress.

## Files likely to change

- `src/Panopticon.Simulation/Simulation/Transport/TransportGraphDomainModel.cs`
- `test/Panopticon.Simulation.Tests/Simulation/TransportStateProjectionTests.cs`
- `docs/roadmap/implementation-checklist.md`, `docs/roadmap/track-a-foundations.md`, `docs/roadmap/README.md`

## Acceptance criteria

- [ ] `vehicle` family residuals are empty at every sampled frame.
- [ ] Existing transport-graph tests pass unmodified.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~TransportStateProjection|FullyQualifiedName~TransportGraphDomain"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No change to transport behavior, topology, or metrics.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
