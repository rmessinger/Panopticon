# Track A · A5: Scenario authoring & visualization implementation plan

Source of truth: [a5-visualization.md](../deep-dives/a5-visualization.md) :
[Architecture diagrams §8](../deep-dives/a5-visualization.md#8-architecture-diagrams). Cards:
[`WI-A5-01..33`](../agent/cards/).

This plan grounds the A5 phase on the recording, scenario, and matrix code that A1/A4 already
landed, and fixes the contracts the remaining cards build on. WI-A5-06 (this document plus the
deep-dive corrections) is the grounding step; WI-A5-01 audits the WorldBuilder baseline and is
documented in [track-a-a5-6-worldbuilder-baseline.md](track-a-a5-6-worldbuilder-baseline.md).

## Landed substrate

| Capability | Landed as | Card |
|---|---|---|
| T0/T1/T2 tiered recording | `Recording.RunRecording.Attach` composing `RunOutputRecorder` (T0), `TieredSignatureRecorder` (T1), `TriggeredDetailCapture` (T2), fed by one `WorldStateSampler` pass per logic frame | WI-A1-05..10 |
| Snapshot/delta reconstruction | `SnapshotDeltaRecorder` + `WorldStateArtifactWriter`/`ReconstructStateAt` | WI-A1-11 |
| Operational recording + rebuild | `OperationalRecorder`, `RunReconstructor` from nearest checkpoint | WI-A1-19..21 |
| Domain-state projection | `DomainStateRecorder` over `IDomainStateProjection`, `DomainId` + `DomainStateSchemaVersion` | WI-A4-44 |
| Run manifest | `RunOutput` JSON (camelCase, string enums), `RunOutput.LoadFromFile`/`SaveToFile` | WI-A1-05 |
| Experiment matrix | `ScenarioMatrix` (`SweepAxis`, `ConstraintFilter`, `AssertionTemplate`) + `ScenarioMatrixRunner` (ReplaySketch.Core) | WI-A4-26 |
| Replay scenario model | `ReplayScenario` (map, players, assertions) + TestHarness `ReplayScenarioRunner` | P0/P9 line |
| Headless image export | `FrameCaptureService` | WI-A1-14 |

## Contracts fixed by this plan

Artifact versions. `RunOutput` is the manifest and stays additively version-tolerant: new
fields are optional and null-omitted, so older readers skip them. Each side artifact carries its
own explicit schema version (`DomainStateSchemaVersion` today; the snapshot/delta world-state
artifact and the operational stream carry theirs in their headers). A loader that meets an
unknown schema version fails with an explicit error naming the artifact, version found, and
version understood; it never guesses.

Bundle-relative paths. Every path recorded inside a `RunOutput` is relative to the run bundle
root. Bundles relocate and archive wholesale; consumers resolve against the root they loaded
the manifest from.

Event format. The timeline event stream is the run's `MetricSamples` (T0) plus the
`ReplayActionMonitor` order/action stream; there is no separate engine event log format, and
A5-3 builds jump-to-frame on these two.

Frame-zero reconstruction. `Full` mode reconstructs any recorded frame, frame 0 included, from
snapshots/deltas; no special initial-state artifact exists or is needed. `Operational` runs
have no recorded frame 0 state; the earliest checkpoint bounds what can be rebuilt.

Capture-from-run. A derived scenario is a deliberately lossy projection of one recorded frame:
it records source run id and frame, keeps placements, parameters, and whatever the frame's
recorded state contains, and carries no per-entity history. Frames rebuilt from operational
checkpoints keep that mode's approximation. Matrix bindings are not inherited; the new scenario
is concrete.

Matrix binding syntax. `AssertionTemplate` values substitute `{axis}` placeholders with the
cell's binding values; `ConstraintFilter.Allows` evaluates its expression against the same
bindings. Binding names are the sweep-axis names. Cells outside a constraint are not dispatched
and are reported in the matrix summary.

Domain-view contract. A domain participates in A5 by implementing `IDomainStateProjection`
(`DomainId`, state extraction, schema version). `RunOutput.DomainId` names the domain that
produced the run; visualizers and comparators dispatch on it. Domains without a projection get
the generic RTS view and the legacy spatial bucket keys in T1.

## Sub-phase to card mapping

| Sub-phase | Cards | Status |
|---|---|---|
| A5-1 Authoring & setup | WI-A5-03, 30 | Not started |
| A5-2 Matrix / sweeps | WI-A5-14, 13 | Not started |
| A5-3 Replayer / visualizer | WI-A5-07, 08, 09, 04, 11, 12 | Not started |
| A5-4 Divergence localization | WI-A5-05, 17, 18, 20, 24, 25, 26, 31 | Not started |
| A5-5 Authoring & viz at scale | WI-A5-32, 33 | Not started |
| A5-6 WorldBuilder baseline | WI-A5-01, 02, 27, 28, 34, 35 | Audit landed (WI-A5-01); repairs pending |

Status lives in [implementation-checklist.md](../implementation-checklist.md) and the phase
table in the deep dive; this table only maps cards.
