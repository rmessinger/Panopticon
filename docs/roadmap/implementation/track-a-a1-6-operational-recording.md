# Track A · A1-6: Operational recording and checkpoint re-simulation

> Audience: human overview above the divider; agent-context detail below it.

Implements checklist item A1-6 of [Track A](../track-a-foundations.md). Design source of truth:
[deep-dives/a1-6-operational-recording.md](../deep-dives/a1-6-operational-recording.md),
including its [architecture diagrams](../deep-dives/a1-6-operational-recording.md#10-architecture-diagrams).

## Scope

Add `Operational` recording beside the existing `Full` mode. Operational runs write a streamed
boundary record and sparse engine checkpoints (default every 6 campaign hours). Granular windows
are rebuilt by re-simulating from the nearest checkpoint; rebuilt frames are not verified.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| A1-6a | [`WI-A1-13`](../agent/cards/WI-A1-13-audit-checkpoint-coverage.md) | Audit checkpoint persistence coverage, checkpoint chunk exclusions, and logic RNG sources. |
| A1-6a | [`WI-A1-14`](../agent/cards/WI-A1-14-persistable-logic-random.md) | Replace non-persistable logic random state with persisted `SageRandom` state. |
| A1-6b | [`WI-A1-15`](../agent/cards/WI-A1-15-engine-checkpoint-format.md) | `PNCK` checkpoint writer and reader. |
| A1-6b | [`WI-A1-16`](../agent/cards/WI-A1-16-restore-host-from-checkpoint.md) | Restore a `SimulationHost` from checkpoint without `StartCampaign`. |
| A1-6c | [`WI-A1-17`](../agent/cards/WI-A1-17-run-identity.md) | Withdrawn: `RunIdentity` removed. |
| A1-6c | [`WI-A1-18`](../agent/cards/WI-A1-18-operational-state-hash.md) | Withdrawn: `OperationalStateHasher` removed. |
| A1-6d | [`WI-A1-19`](../agent/cards/WI-A1-19-recording-modes-and-cadence.md) | `RecordingMode`, `RecordingOptions`, `RecordingCadence`, and `IReconstructableDomain`. |
| A1-6d | [`WI-A1-20`](../agent/cards/WI-A1-20-operational-stream.md) | `operational.bin` writer and reader. |
| A1-6e | [`WI-A1-21`](../agent/cards/WI-A1-21-operational-run-bundle.md) | Bundle layout, checkpoint scheduling, and manifest fields. |
| A1-6f | [`WI-A1-22`](../agent/cards/WI-A1-22-run-reconstructor.md) | `RunReconstructor`, bootstrap, and sinks. |
| A1-6f | [`WI-A1-23`](../agent/cards/WI-A1-23-reconstruction-cache.md) | In-memory LRU cache of derived checkpoints at operational boundaries. |
| A1-6g | [`WI-A1-24`](../agent/cards/WI-A1-24-reconstruction-conformance.md) | RTS and transport-graph conformance: exact checkpoint restore, invariants on rebuilt frames. |
| W3a adapter | [`WI-W3a-31`](../agent/cards/WI-W3a-31-operational-recording-adapter.md) | Western Front `IReconstructableDomain` adapter and rollups. |

## Execution order and dependencies

1. `WI-A1-13` and `WI-A1-19` can start immediately.
2. `WI-A1-14` follows `WI-A1-13`.
3. `WI-A1-15` follows `WI-A1-13`, `WI-A1-14`, and `WI-A4-43`.
4. `WI-A1-16` follows `WI-A1-15`.
5. `WI-A1-20` follows `WI-A1-19` and `WI-A4-44`.
6. `WI-A1-21` follows `WI-A1-15` and `WI-A1-20`.
7. `WI-A1-22` follows `WI-A1-16` and `WI-A1-21`.
8. `WI-A1-23` follows `WI-A1-22`.
9. `WI-A1-24` follows `WI-A1-22`, `WI-A1-23`, and `WI-A4-45`.
10. `WI-W3a-31` follows `WI-W0-07`, `WI-W0-09`, `WI-W3a-12`, `WI-A1-19`, and `WI-A1-22`.

---

## Agent context

## File map

| Card | New files | Edited files |
|---|---|---|
| `WI-A1-13` | `test/Panopticon.Simulation.Tests/SaveLoad/CheckpointCoverageTests.cs` | `SaveFile` chunk-exclusion helper if needed |
| `WI-A1-14` | `test/Panopticon.Simulation.Tests/Reproducibility/PersistableLogicRandomTests.cs` | `GameLogic.cs`, `IGame.cs`, `IRandom.cs` if helper needed |
| `WI-A1-15` | `Recording/EngineCheckpoint.cs`, checkpoint section types, `EngineCheckpointFormatTests.cs` | none |
| `WI-A1-16` | `EngineCheckpointRestoreTests.cs` | `EngineCheckpoint.cs`, narrow `SaveFile` restore seam |
| `WI-A1-19` | `Recording/RecordingMode.cs`, `Recording/RecordingOptions.cs`, `Simulation/IReconstructableDomain.cs`, tests | `RunRecording.cs` |
| `WI-A1-20` | `OperationalStreamWriter.cs`, `OperationalStreamReader.cs`, `OperationalRecords.cs`, tests | none |
| `WI-A1-21` | `Recording/OperationalRecorder.cs`, bundle tests | `RunRecording.cs`, `RunOutput.cs` |
| `WI-A1-22` | `RunReconstructor.cs`, `IReconstructionBootstrap.cs`, sink types, tests | none |
| `WI-A1-23` | `ReconstructionCache.cs`, cache tests | `RunReconstructor.cs` |
| `WI-A1-24` | `ReconstructionConformanceTests.cs` | existing recording tests only for fixture reuse |
| `WI-W3a-31` | `WesternFrontOperationalRecordingTests.cs` | `WesternFrontDomainModel.cs`, schema owner, scheduler |

## Invariants

- `Full` remains the default recording mode. Existing `Full` outputs stay byte-identical.
- New `RunOutput` manifest fields are nullable and use `JsonIgnoreCondition.WhenWritingNull`.
- Checkpoints exclude presentation chunks. Logic chunks use the existing `SaveFile.Persist` path.
- `GameLogic.Random` state is persistable and included in checkpoints.
- Checkpoints are recording artifacts, not player save games.
- Restore for reconstruction bypasses `StartCampaign` when a host is already booted for the same scenario.
- Determinism is not assumed. Frames after a checkpoint are rebuilt, not verified; the operational stream is the authoritative record.
- Restore at a checkpoint frame is exact: the domain-state section equals a fresh `CaptureState`.
- The reconstruction cache is in memory only; nothing is written outside the bundle.
- Default checkpoint interval: 648,000 frames (6 h at 30 Hz).
- The operational stream is crash-tolerant at chunk boundaries. A missing footer index is rebuilt by scanning completed chunks.
- Existing determinism-enforcing tests (same-seed, golden baselines) keep the repository stop-and-report rule. Rebuilt-frame drift is not such a failure.

Code hooks: see the deep dive's
[appendix](../deep-dives/a1-6-operational-recording.md#appendix-current-state-hooks).
