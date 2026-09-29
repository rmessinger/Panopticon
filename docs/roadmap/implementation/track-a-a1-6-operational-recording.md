# Track A · A1-6: Operational recording and seeded reconstruction

> Audience: human overview above the divider; agent-context detail below it.

Implements checklist item A1-6 of [Track A](../track-a-foundations.md). Design source of truth:
[deep-dives/a1-6-operational-recording.md](../deep-dives/a1-6-operational-recording.md),
including its [architecture diagrams](../deep-dives/a1-6-operational-recording.md#10-architecture-diagrams).

## Scope

Add `Operational` recording beside the existing `Full` mode. Operational runs write a streamed
boundary record, periodic engine checkpoints, a run identity, and verification hashes. Granular
windows reconstruct from the nearest checkpoint and stop on the first hash divergence.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| A1-6a | [`WI-A1-13`](../agent/cards/WI-A1-13-audit-checkpoint-coverage.md) | Audit checkpoint persistence coverage, checkpoint chunk exclusions, and logic RNG sources. |
| A1-6a | [`WI-A1-14`](../agent/cards/WI-A1-14-persistable-logic-random.md) | Replace non-persistable logic random state with persisted `SageRandom` state. |
| A1-6b | [`WI-A1-15`](../agent/cards/WI-A1-15-engine-checkpoint-format.md) | `PNCK` checkpoint writer and reader. |
| A1-6b | [`WI-A1-16`](../agent/cards/WI-A1-16-restore-host-from-checkpoint.md) | Restore a `SimulationHost` from checkpoint without `StartCampaign`. |
| A1-6c | [`WI-A1-17`](../agent/cards/WI-A1-17-run-identity.md) | `RunIdentity` and stable identity hash. |
| A1-6c | [`WI-A1-18`](../agent/cards/WI-A1-18-operational-state-hash.md) | `OperationalStateHasher` world, bucket, and globals hashes. |
| A1-6d | [`WI-A1-19`](../agent/cards/WI-A1-19-recording-modes-and-cadence.md) | `RecordingMode`, `RecordingOptions`, `RecordingCadence`, and `IReconstructableDomain`. |
| A1-6d | [`WI-A1-20`](../agent/cards/WI-A1-20-operational-stream.md) | `operational.bin` writer and reader. |
| A1-6e | [`WI-A1-21`](../agent/cards/WI-A1-21-operational-run-bundle.md) | Bundle layout, checkpoint scheduling, and manifest fields. |
| A1-6f | [`WI-A1-22`](../agent/cards/WI-A1-22-run-reconstructor.md) | `RunReconstructor`, bootstrap, sinks, identity refusal, and divergence reporting. |
| A1-6f | [`WI-A1-23`](../agent/cards/WI-A1-23-reconstruction-cache.md) | LRU cache of derived checkpoints at operational boundaries. |
| A1-6g | [`WI-A1-24`](../agent/cards/WI-A1-24-reconstruction-conformance.md) | RTS and transport-graph conformance evidence. |
| W3a adapter | [`WI-W3a-31`](../agent/cards/WI-W3a-31-operational-recording-adapter.md) | Western Front `IReconstructableDomain` adapter and rollups. |

## Execution order and dependencies

1. `WI-A1-13`, `WI-A1-17`, and `WI-A1-19` can start immediately.
2. `WI-A1-14` follows `WI-A1-13`.
3. `WI-A1-15` follows `WI-A1-13`, `WI-A1-14`, and `WI-A4-43`.
4. `WI-A1-16` follows `WI-A1-15`.
5. `WI-A1-18` follows `WI-A1-13`.
6. `WI-A1-20` follows `WI-A1-18`, `WI-A1-19`, and `WI-A4-44`.
7. `WI-A1-21` follows `WI-A1-15`, `WI-A1-17`, and `WI-A1-20`.
8. `WI-A1-22` follows `WI-A1-16` and `WI-A1-21`.
9. `WI-A1-23` follows `WI-A1-22`.
10. `WI-A1-24` follows `WI-A1-22`, `WI-A1-23`, and `WI-A4-45`.
11. `WI-W3a-31` follows `WI-W0-07`, `WI-W0-09`, `WI-W3a-12`, `WI-A1-19`, and `WI-A1-22`.

---

## Agent context

## File map

| Card | New files | Edited files |
|---|---|---|
| `WI-A1-13` | `test/Panopticon.Simulation.Tests/SaveLoad/CheckpointCoverageTests.cs` | `SaveFile` chunk-exclusion helper if needed |
| `WI-A1-14` | `test/Panopticon.Simulation.Tests/Reproducibility/PersistableLogicRandomTests.cs` | `GameLogic.cs`, `IGame.cs`, `IRandom.cs` if helper needed |
| `WI-A1-15` | `Recording/EngineCheckpoint.cs`, checkpoint section types, `EngineCheckpointFormatTests.cs` | none |
| `WI-A1-16` | `EngineCheckpointRestoreTests.cs` | `EngineCheckpoint.cs`, narrow `SaveFile` restore seam |
| `WI-A1-17` | `Recording/RunIdentity.cs`, `RunIdentityTests.cs` | `Directory.Packages.props`, optionally `RunOutput.cs` |
| `WI-A1-18` | `Recording/OperationalStateHasher.cs`, `OperationalStateHashes.cs`, tests | none |
| `WI-A1-19` | `Recording/RecordingMode.cs`, `Recording/RecordingOptions.cs`, `Simulation/IReconstructableDomain.cs`, tests | `RunRecording.cs` |
| `WI-A1-20` | `OperationalStreamWriter.cs`, `OperationalStreamReader.cs`, `OperationalRecords.cs`, tests | none |
| `WI-A1-21` | `Recording/OperationalRecorder.cs`, bundle tests | `RunRecording.cs`, `RunOutput.cs` |
| `WI-A1-22` | `RunReconstructor.cs`, `IReconstructionBootstrap.cs`, sink and divergence types, tests | none |
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
- Operational hashes use the same bytes that checkpoint persistence writes.
- Determinism is scoped to one run identity: build, runtime, operating system, architecture, SIMD flags, inputs, seed, and shard layout.
- Identity mismatch is refused by default. `AllowIdentityMismatch` still requires hash verification.
- The operational stream is crash-tolerant at chunk boundaries. A missing footer index is rebuilt by scanning completed chunks.
- Determinism failures follow the repository stop-and-report rule. Same-seed, golden-baseline, and output byte-identity deviations are not fixed or regenerated without operator determination.

Code hooks: see the deep dive's
[appendix](../deep-dives/a1-6-operational-recording.md#appendix-current-state-hooks).
