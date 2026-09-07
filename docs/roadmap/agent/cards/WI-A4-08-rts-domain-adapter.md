# Task Card `WI-A4-08`: Implement the RTS domain adapter

| Field | Value |
|---|---|
| Card ID | `WI-A4-08` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-1 Simulation Domain Model seam |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A4-07` |
| Diagrams | A4 domain diagrams created by `WI-A4-06` |

## Goal

Move RTS-specific entity, spatial-topology, authority, metric, and recording-bucket behavior behind
the domain contracts while preserving observable runs.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-simulation-domain-model.md`: RTS mapping.
- `src/OpenSage.Game/Logic/GameLogic.cs`, `Logic/PartitionCellManager.cs`.
- `src/OpenSage.Game/Recording/SignatureTree.cs`, `RunRecording.cs`.
- `src/OpenSage.Game/RunOutput.cs`.
- `src/OpenSage.Game.Tests/SimulationHostTests.cs` and recording tests.

## Steps

1. Add the RTS adapter at the design-specified location.
2. Delegate entity enumeration, spatial bucket keys, authority, metrics, and recording projection
   from existing callers to the adapter.
3. Keep current bucket origins, quantization, metric names, and object iteration behavior.
4. Add equivalence tests using a fixed mocked run.

## Files likely to change

- New RTS adapter under `src/OpenSage.Game/Simulation/`
- `src/OpenSage.Game/Recording/`
- Focused tests under `src/OpenSage.Game.Tests/`

## Acceptance criteria

- [ ] Existing RTS output and signature tests pass unchanged.
- [ ] No generic host or recorder performs an RTS-only type check.
- [ ] The adapter exposes every responsibility required by A4-1.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SimulationHostTests|FullyQualifiedName~TieredRecordingTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No storage, pathfinding, partitioning, or fidelity optimization.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-09`.
