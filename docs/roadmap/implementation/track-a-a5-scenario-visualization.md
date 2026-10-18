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
| A5-1 Authoring & setup | WI-A5-03, 06, 10..14, 23, 30, 32..33 | Done: scenario artifact + loader (03), validation (10), generators (23), capture-from-run (11), preview (13), population/parameter/topology editors (14/32/33), WorldBuilder binding (30) |
| A5-2 Matrix / sweeps | WI-A5-15, 16 | Done: declarative typed bindings + serializable filters (15), keyed per-cell run-bundle sets (16) |
| A5-3 Replayer / visualizer | WI-A5-04, 07..09, 11, 12, 18..21, 24, 25, 29, 31 | Done: bundles (07..09), timeline/charts/events (04/12), domain-state rendering + playback + export (18..20/31), regional detail (21/29), streaming/culling + LOD (24/25) |
| A5-4 Divergence localization | WI-A5-05, 22 | Done: comparator + leaf-most loci (05), T2 detail matching (22); T0-invariant arming designed into the grounding doc (06) |
| A5-5 Authoring & viz at scale | WI-A5-23, 24..26, 29 | Done: procedural generation (23), bounded loading (29), streaming/LOD (24/25), B1 throughput (26); budgets below |
| A5-6 WorldBuilder baseline | WI-A5-01, 02, 27, 28, 34, 35 | Done: [audit + baseline](track-a-a5-6-worldbuilder-baseline.md) (01), repairs (27/28/34/35), round-trip + engine play leg (02) |

Status lives in [implementation-checklist.md](../implementation-checklist.md) and the phase
table in the deep dive; this table only maps cards.

## Scale budgets and B1 benchmarks (WI-A5-26)

The A5 throughput benchmark (`a5.throughput`, `src/Panopticon.Benchmarks.Simulation/A5ThroughputBenchmark.cs`)
measures the authoring and replay pipeline on code-authored fixtures at full scale. Five operations,
each a `system.*.spanMs` metric inside one B1 `BenchmarkRunRecord` (`b1.run/1`):

| Benchmark ID | Operation | Full-scale fixture |
|---|---|---|
| `a5.expand` | `ScenarioGeneratorService.Expand` | 10,000 entities + 10,000 commands |
| `a5.serialize` | scenario JSON serialization (UTF-8) | expanded 10k/10k scenario |
| `a5.bundleOpen` | `RunBundle.Load` + state-artifact index scan | 250-frame recording of 10,000 entities |
| `a5.randomSeek` | 5 random `ReconstructStateAt` seeks | same recording |
| `a5.visibleProjection` | whole-frame projection + viewport query + LOD batching | same recording; 33.3 ms visible-frame anchor applies |

Budget policy: no committed thresholds. Regressions follow B1's rule, confidence-interval
non-overlap against a caller-supplied baseline record (`--baseline <path>`); `tick.wallMs` and
`tick.allocBytes` carry the per-iteration totals. The full-scale plan constants are asserted by
`A5BenchmarkTests.FullScalePlan_MeetsDocumentedSizes`. A5 keeps no standalone stopwatch gates;
the former expansion-time assert in `ProceduralScenarioTests` was removed in favor of these
metrics. These budgets are the A5-5 linkage promised in the checklist: authoring a large
procedural scenario and replaying a long/large run are tracked here, not by ad-hoc timers.
