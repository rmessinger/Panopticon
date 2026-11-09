# Legacy Card Mapping

Single lookup from legacy agent card files to their canonical OpenProject tickets. OpenProject
(project Panopticon, id 4) stays canonical for ticket status, prerequisites, descriptions, and
completion records; this table is a name-to-ticket index and mirrors none of them.

The table replaces the 734 per-ticket link-stub files that lived under `agent/cards/`: each row
preserves one deleted stub's pairing, one-to-one. Rows are sorted by card ID, so any card ID or
filename resolves to its row deterministically. `Legacy card file` is the deleted stub's filename,
kept verbatim so old references and history stay resolvable. `Original card ID` carries the
pre-split ID for tickets that replaced a split card (WI-P0-14a and WI-P0-14b split WI-P0-14); it is
empty for all other rows.

Card content from before the OpenProject cutover is in git history at commit
c94289f24d4d882b6f0009d116773d9fb595a350 ([browse the old
cards](https://github.com/rmessinger/Panopticon/tree/c94289f24d4d882b6f0009d116773d9fb595a350/docs/roadmap/agent/cards)).
The non-ticket data file in `agent/cards/` remains there: `WI-M0-03-deployment-proof.json`. The
community map records moved to [`data/community-map-records.md`](../data/community-map-records.md).

New tickets append one row here instead of creating a stub file; see [ticket
workflow](README.md#ticket-workflow) and the [template](task-card-template.md).
| `WI-A1-01` |  | `WI-A1-01-audit-graphics-device-couplings.md` | Audit `GraphicsDevice` couplings on the logic and asset-data path | https://projects.reid-corp.com/work_packages/222 |
| `WI-A1-02` |  | `WI-A1-02-audit-tick-presentation-couplings.md` | Audit presentation couplings inside the tick path | https://projects.reid-corp.com/work_packages/223 |
| `WI-A1-03` |  | `WI-A1-03-populate-architecture-diagrams.md` | Replace the A1 architecture-diagram placeholder with grounded diagrams | https://projects.reid-corp.com/work_packages/224 |
| `WI-A1-04` |  | `WI-A1-04-data-only-asset-loading.md` | Data-only asset loading without a graphics device | https://projects.reid-corp.com/work_packages/225 |
| `WI-A1-05` |  | `WI-A1-05-extract-logic-stepper.md` | Extract the logic tick into a renderer-free stepper | https://projects.reid-corp.com/work_packages/226 |
| `WI-A1-06` |  | `WI-A1-06-simulation-host.md` | Introduce `SimulationHost` with faster-than-real-time stepping | https://projects.reid-corp.com/work_packages/227 |
| `WI-A1-07` |  | `WI-A1-07-run-outputs-metrics-stream.md` | Emit metrics and events run output from a headless run | https://projects.reid-corp.com/work_packages/228 |
| `WI-A1-08` |  | `WI-A1-08-testharness-simulation-host.md` | Migrate the TestHarness headless loop onto `SimulationHost` | https://projects.reid-corp.com/work_packages/229 |
| `WI-A1-09` |  | `WI-A1-09-t1-signature-tree-recording.md` | T1 hierarchical signature-tree recording | https://projects.reid-corp.com/work_packages/230 |
| `WI-A1-10` |  | `WI-A1-10-t2-triggered-detail-capture.md` | T2 triggered detail capture with a bounded ring buffer | https://projects.reid-corp.com/work_packages/231 |
| `WI-A1-11` |  | `WI-A1-11-state-snapshot-delta-recording.md` | State snapshot and delta recording | https://projects.reid-corp.com/work_packages/232 |
| `WI-A1-12` |  | `WI-A1-12-sleepy-update-stall.md` | Fix the sleepy-update stall at GLA hole destruction | https://projects.reid-corp.com/work_packages/233 |
| `WI-A1-13` |  | `WI-A1-13-audit-checkpoint-coverage.md` | Audit checkpoint coverage | https://projects.reid-corp.com/work_packages/234 |
| `WI-A1-14` |  | `WI-A1-14-persistable-logic-random.md` | Make logic random persistable | https://projects.reid-corp.com/work_packages/235 |
| `WI-A1-15` |  | `WI-A1-15-engine-checkpoint-format.md` | Add the engine checkpoint format | https://projects.reid-corp.com/work_packages/236 |
| `WI-A1-16` |  | `WI-A1-16-restore-host-from-checkpoint.md` | Restore host from checkpoint | https://projects.reid-corp.com/work_packages/237 |
| `WI-A1-17` |  | `WI-A1-17-run-identity.md` | Capture run identity | https://projects.reid-corp.com/work_packages/238 |
| `WI-A1-18` |  | `WI-A1-18-operational-state-hash.md` | Hash operational state | https://projects.reid-corp.com/work_packages/239 |
| `WI-A1-19` |  | `WI-A1-19-recording-modes-and-cadence.md` | Add recording modes and cadence | https://projects.reid-corp.com/work_packages/240 |
| `WI-A1-20` |  | `WI-A1-20-operational-stream.md` | Write the operational stream | https://projects.reid-corp.com/work_packages/241 |
| `WI-A1-21` |  | `WI-A1-21-operational-run-bundle.md` | Assemble operational run bundle | https://projects.reid-corp.com/work_packages/242 |
| `WI-A1-22` |  | `WI-A1-22-run-reconstructor.md` | Reconstruct operational runs | https://projects.reid-corp.com/work_packages/243 |
| `WI-A1-23` |  | `WI-A1-23-reconstruction-cache.md` | Cache reconstructed checkpoints | https://projects.reid-corp.com/work_packages/244 |
| `WI-A1-24` |  | `WI-A1-24-reconstruction-conformance.md` | Prove reconstruction conformance | https://projects.reid-corp.com/work_packages/245 |
| `WI-A2-01` |  | `WI-A2-01-audit-randomness-sources.md` | Audit randomness sources and classify them as seeded or uncontrolled | https://projects.reid-corp.com/work_packages/246 |
| `WI-A2-02` |  | `WI-A2-02-audit-nonrandom-nondeterminism.md` | Audit non-random nondeterminism on the logic path | https://projects.reid-corp.com/work_packages/247 |
| `WI-A2-03` |  | `WI-A2-03-route-randomness-through-seeded-source.md` | Route simulation-affecting randomness through the seeded source | https://projects.reid-corp.com/work_packages/248 |
| `WI-A2-04` |  | `WI-A2-04-stability-verification-harness.md` | Statistical verification harness | https://projects.reid-corp.com/work_packages/249 |
| `WI-A3-01` |  | `WI-A3-01-audit-logic-presentation-reads.md` | Audit logic reads of render-only and audio-only state | https://projects.reid-corp.com/work_packages/250 |
| `WI-A3-02` |  | `WI-A3-02-logic-presentation-boundary-test.md` | Lock the logic-to-presentation boundary with an enforcement test | https://projects.reid-corp.com/work_packages/251 |
| `WI-A4-01` |  | `WI-A4-01-extract-ai-decision-policy-interface.md` | Extract `IAIDecisionPolicy` seam, `SkirmishAIPlayer` as reference policy | https://projects.reid-corp.com/work_packages/252 |
| `WI-A4-02` |  | `WI-A4-02-ai-policy-selection-factory.md` | AI policy-selection config + factory | https://projects.reid-corp.com/work_packages/253 |
| `WI-A4-03` |  | `WI-A4-03-design-neural-network-policy.md` | Design the first neural-network `IAIDecisionPolicy` | https://projects.reid-corp.com/work_packages/254 |
| `WI-A4-04` |  | `WI-A4-04-implement-neural-network-policy.md` | Implement `NeuralNetworkAIDecisionPolicy` (inference only) | https://projects.reid-corp.com/work_packages/255 |
| `WI-A4-05` |  | `WI-A4-05-audit-simulation-domain-assumptions.md` | Audit simulation-domain assumptions | https://projects.reid-corp.com/work_packages/256 |
| `WI-A4-06` |  | `WI-A4-06-design-simulation-domain-contracts.md` | Design simulation-domain contracts | https://projects.reid-corp.com/work_packages/257 |
| `WI-A4-07` |  | `WI-A4-07-add-simulation-domain-contracts.md` | Add simulation-domain contracts to the host | https://projects.reid-corp.com/work_packages/258 |
| `WI-A4-08` |  | `WI-A4-08-rts-domain-adapter.md` | Implement the RTS domain adapter | https://projects.reid-corp.com/work_packages/259 |
| `WI-A4-09` |  | `WI-A4-09-transport-graph-domain.md` | Add the minimal transport/graph domain | https://projects.reid-corp.com/work_packages/260 |
| `WI-A4-10` |  | `WI-A4-10-cross-domain-host-contract.md` | Prove the cross-domain host contract | https://projects.reid-corp.com/work_packages/261 |
| `WI-A4-11` |  | `WI-A4-11-design-neural-policy-evolution.md` | Design flat neural-policy evolution | https://projects.reid-corp.com/work_packages/262 |
| `WI-A4-12` |  | `WI-A4-12-carry-ai-policy-payload.md` | Carry AI-policy configuration into player construction | https://projects.reid-corp.com/work_packages/263 |
| `WI-A4-13` |  | `WI-A4-13-neural-population-operators.md` | Add neural population and variation operators | https://projects.reid-corp.com/work_packages/264 |
| `WI-A4-14` |  | `WI-A4-14-neural-policy-fitness.md` | Add neural-policy fitness scoring | https://projects.reid-corp.com/work_packages/265 |
| `WI-A4-15` |  | `WI-A4-15-run-neural-policy-generation.md` | Run and report one neural-policy generation | https://projects.reid-corp.com/work_packages/266 |
| `WI-A4-16` |  | `WI-A4-16-neural-training-integration.md` | Integrate neural-policy training with headless matches | https://projects.reid-corp.com/work_packages/267 |
| `WI-A4-17` |  | `WI-A4-17-ground-hierarchical-ai-design.md` | Ground the hierarchical-AI design | https://projects.reid-corp.com/work_packages/268 |
| `WI-A4-18` |  | `WI-A4-18-squad-model-lifecycle.md` | Add squad model and lifecycle | https://projects.reid-corp.com/work_packages/269 |
| `WI-A4-19` |  | `WI-A4-19-persist-squad-hierarchy.md` | Persist squad hierarchy state | https://projects.reid-corp.com/work_packages/270 |
| `WI-A4-20` |  | `WI-A4-20-soldier-decision-policy-seam.md` | Extract the soldier decision-policy seam | https://projects.reid-corp.com/work_packages/271 |
| `WI-A4-21` |  | `WI-A4-21-commander-decision-policy.md` | Add the commander decision-policy seam | https://projects.reid-corp.com/work_packages/272 |
| `WI-A4-22` |  | `WI-A4-22-general-policy-and-hierarchy-adapter.md` | Add the general policy and hierarchy adapter | https://projects.reid-corp.com/work_packages/273 |
| `WI-A4-23` |  | `WI-A4-23-hierarchy-reference-contract.md` | Lock the hierarchy reference contract | https://projects.reid-corp.com/work_packages/274 |
| `WI-A4-24` |  | `WI-A4-24-design-hierarchical-neural-policies.md` | Design hierarchical neural policies | https://projects.reid-corp.com/work_packages/275 |
| `WI-A4-25` |  | `WI-A4-25-neural-soldier-policy.md` | Implement the neural soldier policy | https://projects.reid-corp.com/work_packages/276 |
| `WI-A4-26` |  | `WI-A4-26-neural-commander-policy.md` | Implement the neural commander policy | https://projects.reid-corp.com/work_packages/277 |
| `WI-A4-27` |  | `WI-A4-27-neural-general-policy.md` | Implement the neural general policy | https://projects.reid-corp.com/work_packages/278 |
| `WI-A4-28` |  | `WI-A4-28-hierarchical-neural-inference.md` | Prove hierarchical neural inference | https://projects.reid-corp.com/work_packages/279 |
| `WI-A4-29` |  | `WI-A4-29-design-hierarchical-coevolution.md` | Design hierarchical co-evolution | https://projects.reid-corp.com/work_packages/280 |
| `WI-A4-30` |  | `WI-A4-30-run-hierarchical-coevolution.md` | Run and report hierarchical co-evolution | https://projects.reid-corp.com/work_packages/281 |
| `WI-A4-31` |  | `WI-A4-31-audit-experiment-seam-ownership.md` | Audit remaining experiment-seam ownership | https://projects.reid-corp.com/work_packages/282 |
| `WI-A4-32` |  | `WI-A4-32-extract-pathfinding-seam.md` | Extract the pathfinding/routing seam | https://projects.reid-corp.com/work_packages/283 |
| `WI-A4-33` |  | `WI-A4-33-extract-entity-storage-seam.md` | Extract the entity-storage seam | https://projects.reid-corp.com/work_packages/284 |
| `WI-A4-34` |  | `WI-A4-34-extract-partition-scheme-seam.md` | Extract the partition-scheme seam | https://projects.reid-corp.com/work_packages/285 |
| `WI-A4-35` |  | `WI-A4-35-extract-fidelity-policy-seam.md` | Extract the fidelity-policy seam | https://projects.reid-corp.com/work_packages/286 |
| `WI-A4-36` |  | `WI-A4-36-register-model-import-seam.md` | Register the model-import seam | https://projects.reid-corp.com/work_packages/287 |
| `WI-A4-37` |  | `WI-A4-37-design-experiment-harness.md` | Design the experiment harness | https://projects.reid-corp.com/work_packages/288 |
| `WI-A4-38` |  | `WI-A4-38-experiment-variant-registry.md` | Add experiment definitions and variant registry | https://projects.reid-corp.com/work_packages/289 |
| `WI-A4-39` |  | `WI-A4-39-run-b1-experiments.md` | Run configured B1 experiments | https://projects.reid-corp.com/work_packages/290 |
| `WI-A4-40` |  | `WI-A4-40-write-experiment-comparison-report.md` | Write the experiment comparison report | https://projects.reid-corp.com/work_packages/291 |
| `WI-A4-41` |  | `WI-A4-41-compare-two-seam-implementations.md` | Compare two seam implementations end to end | https://projects.reid-corp.com/work_packages/292 |
| `WI-A4-42` |  | `WI-A4-42-domain-state-contracts.md` | Add domain state projection contracts | https://projects.reid-corp.com/work_packages/293 |
| `WI-A4-43` |  | `WI-A4-43-domain-state-frames.md` | Persist, diff, and reconstruct domain state frames | https://projects.reid-corp.com/work_packages/294 |
| `WI-A4-44` |  | `WI-A4-44-domain-state-recorder.md` | Record domain state through the run recording | https://projects.reid-corp.com/work_packages/295 |
| `WI-A4-45` |  | `WI-A4-45-transport-state-projection.md` | Implement the state projection on the transport-graph domain | https://projects.reid-corp.com/work_packages/296 |
| `WI-A5-01` |  | `WI-A5-01-worldbuilder-functional-audit.md` | Audit WorldBuilder's functional state and document gaps for B4a | https://projects.reid-corp.com/work_packages/297 |
| `WI-A5-02` |  | `WI-A5-02-worldbuilder-roundtrip-test.md` | Add the WorldBuilder author, save, load, play round-trip test | https://projects.reid-corp.com/work_packages/298 |
| `WI-A5-03` |  | `WI-A5-03-scenario-artifact-and-loader.md` | Define the declarative scenario artifact on top of the P0 harness | https://projects.reid-corp.com/work_packages/299 |
| `WI-A5-04` |  | `WI-A5-04-results-replayer-load-and-scrub.md` | Load and scrub a headless run in the ReplaySketch results view | https://projects.reid-corp.com/work_packages/300 |
| `WI-A5-05` |  | `WI-A5-05-divergence-localization.md` | Localize the first divergence between two runs | https://projects.reid-corp.com/work_packages/301 |
| `WI-A5-06` |  | `WI-A5-06-ground-a5-architecture.md` | Ground A5 architecture and artifact contracts | https://projects.reid-corp.com/work_packages/302 |
| `WI-A5-07` |  | `WI-A5-07-version-run-bundle-manifest.md` | Version the portable run-bundle manifest | https://projects.reid-corp.com/work_packages/303 |
| `WI-A5-08` |  | `WI-A5-08-indexed-run-state-reader.md` | Add an indexed bounded-memory run-state reader | https://projects.reid-corp.com/work_packages/304 |
| `WI-A5-09` |  | `WI-A5-09-write-complete-run-bundles.md` | Emit complete run bundles from the harness | https://projects.reid-corp.com/work_packages/305 |
| `WI-A5-10` |  | `WI-A5-10-validate-declarative-scenarios.md` | Validate declarative scenarios before execution | https://projects.reid-corp.com/work_packages/306 |
| `WI-A5-11` |  | `WI-A5-11-capture-scenario-from-run.md` | Capture a scenario from a recorded frame | https://projects.reid-corp.com/work_packages/307 |
| `WI-A5-12` |  | `WI-A5-12-domain-visualization-projection.md` | Add the domain visualization projection | https://projects.reid-corp.com/work_packages/308 |
| `WI-A5-13` |  | `WI-A5-13-short-scenario-preview.md` | Add a bounded short scenario preview | https://projects.reid-corp.com/work_packages/309 |
| `WI-A5-14` |  | `WI-A5-14-visual-initial-population-editor.md` | Add the initial-population edit controller | https://projects.reid-corp.com/work_packages/310 |
| `WI-A5-15` |  | `WI-A5-15-declarative-experiment-matrices.md` | Make experiment matrices declarative | https://projects.reid-corp.com/work_packages/311 |
| `WI-A5-16` |  | `WI-A5-16-persist-matrix-run-sets.md` | Persist keyed matrix run sets | https://projects.reid-corp.com/work_packages/312 |
| `WI-A5-17` |  | `WI-A5-17-matrix-comparison-workspace.md` | Add the matrix comparison workspace | https://projects.reid-corp.com/work_packages/313 |
| `WI-A5-18` |  | `WI-A5-18-render-recorded-domain-state.md` | Render recorded domain state in Results | https://projects.reid-corp.com/work_packages/314 |
| `WI-A5-19` |  | `WI-A5-19-fidelity-overlay.md` | Add the fidelity overlay and legend | https://projects.reid-corp.com/work_packages/315 |
| `WI-A5-20` |  | `WI-A5-20-headless-results-export.md` | Export result views headlessly | https://projects.reid-corp.com/work_packages/316 |
| `WI-A5-21` |  | `WI-A5-21-configure-regional-t2-capture.md` | Configure regional T2 detail capture | https://projects.reid-corp.com/work_packages/317 |
| `WI-A5-22` |  | `WI-A5-22-inspect-t2-divergence-detail.md` | Inspect T2 detail from a divergence | https://projects.reid-corp.com/work_packages/318 |
| `WI-A5-23` |  | `WI-A5-23-procedural-scenario-generators.md` | Add procedural scenario generators | https://projects.reid-corp.com/work_packages/319 |
| `WI-A5-24` |  | `WI-A5-24-stream-and-cull-replay-state.md` | Stream and cull replay state | https://projects.reid-corp.com/work_packages/320 |
| `WI-A5-25` |  | `WI-A5-25-reuse-b5-replay-rendering.md` | Reuse B5 LOD and instancing in replay | https://projects.reid-corp.com/work_packages/321 |
| `WI-A5-26` |  | `WI-A5-26-benchmark-authoring-and-replay.md` | Track A5 throughput in B1 | https://projects.reid-corp.com/work_packages/322 |
| `WI-A5-27` |  | `WI-A5-27-fix-worldbuilder-edit-application.md` | Fix WorldBuilder edit application ownership | https://projects.reid-corp.com/work_packages/323 |
| `WI-A5-28` |  | `WI-A5-28-make-new-maps-writable.md` | Make new WorldBuilder maps structurally writable | https://projects.reid-corp.com/work_packages/324 |
| `WI-A5-29` |  | `WI-A5-29-region-indexed-state-artifact.md` | Add region-indexed state records | https://projects.reid-corp.com/work_packages/325 |
| `WI-A5-30` |  | `WI-A5-30-worldbuilder-scenario-population.md` | Bind spatial scenario populations in WorldBuilder | https://projects.reid-corp.com/work_packages/326 |
| `WI-A5-31` |  | `WI-A5-31-results-playback-controls.md` | Add replay playback and event navigation | https://projects.reid-corp.com/work_packages/327 |
| `WI-A5-32` |  | `WI-A5-32-visual-scenario-parameter-editor.md` | Add visual scenario-parameter editing | https://projects.reid-corp.com/work_packages/328 |
| `WI-A5-33` |  | `WI-A5-33-nonspatial-topology-editor.md` | Add non-spatial topology editing | https://projects.reid-corp.com/work_packages/329 |
| `WI-A5-34` |  | `WI-A5-34-persist-map-companions.md` | Persist the map.ini and map.str companions | https://projects.reid-corp.com/work_packages/330 |
| `WI-A5-35` |  | `WI-A5-35-engine-valid-objects-and-playability.md` | Engine-valid objects and new-map playability defaults | https://projects.reid-corp.com/work_packages/331 |
| `WI-AV-01` |  | `WI-AV-01-add-ensure-helper.md` | Add the `Ensure` helper and tests | https://projects.reid-corp.com/work_packages/332 |
| `WI-AV-02` |  | `WI-AV-02-convert-null-checks.md` | Convert bare null checks to `ThrowIfNull` | https://projects.reid-corp.com/work_packages/333 |
| `WI-AV-03` |  | `WI-AV-03-convert-string-and-element-checks.md` | Convert string and null-element checks | https://projects.reid-corp.com/work_packages/334 |
| `WI-AV-04` |  | `WI-AV-04-convert-range-and-finite-checks.md` | Convert range and finite checks, update tests | https://projects.reid-corp.com/work_packages/335 |
| `WI-AV-05` |  | `WI-AV-05-collapse-compound-checks.md` | Collapse compound and unique checks | https://projects.reid-corp.com/work_packages/336 |
| `WI-AV-06` |  | `WI-AV-06-enforce-and-document.md` | Analyzer enforcement and style rule | https://projects.reid-corp.com/work_packages/337 |
| `WI-B1-01` |  | `WI-B1-01-benchmark-audit-contract.md` | Audit benchmark hooks and report contract | https://projects.reid-corp.com/work_packages/338 |
| `WI-B1-02` |  | `WI-B1-02-macro-harness-skeleton.md` | Create the macro benchmark harness skeleton | https://projects.reid-corp.com/work_packages/339 |
| `WI-B1-03` |  | `WI-B1-03-tick-system-metrics.md` | Record tick, allocation, GC, memory, and system spans | https://projects.reid-corp.com/work_packages/340 |
| `WI-B1-04` |  | `WI-B1-04-report-writers.md` | Write JSON run records and CSV summaries | https://projects.reid-corp.com/work_packages/341 |
| `WI-B1-05` |  | `WI-B1-05-statistics-regression.md` | Add median bootstrap confidence intervals and regression comparison | https://projects.reid-corp.com/work_packages/342 |
| `WI-B1-06` |  | `WI-B1-06-reference-scenarios.md` | Add seeded reference benchmark scenarios | https://projects.reid-corp.com/work_packages/343 |
| `WI-B1-07` |  | `WI-B1-07-consumer-measurements.md` | Add B4a, B4b, and Western Front benchmark adapters | https://projects.reid-corp.com/work_packages/344 |
| `WI-B4-01` |  | `WI-B4-01-partition-counters.md` | Partition counters and hot-path audit | https://projects.reid-corp.com/work_packages/345 |
| `WI-B4-02` |  | `WI-B4-02-pair-once-partition-update.md` | Allocation-free, pair-once partition update | https://projects.reid-corp.com/work_packages/346 |
| `WI-B4-03` |  | `WI-B4-03-full-scale-verification.md` | Full-scale verification re-run | https://projects.reid-corp.com/work_packages/347 |
| `WI-B4-04` |  | `WI-B4-04-group-movement.md` | Group movement service | https://projects.reid-corp.com/work_packages/348 |
| `WI-B4-05` |  | `WI-B4-05-async-path-requests.md` | Asynchronous path requests | https://projects.reid-corp.com/work_packages/349 |
| `WI-B4-06` |  | `WI-B4-06-hierarchical-gate.md` | Hierarchical pathfinding gate | https://projects.reid-corp.com/work_packages/350 |
| `WI-B4a-01` |  | `WI-B4a-01-chunk-contract.md` | Ground the B4a-5 chunk contract | https://projects.reid-corp.com/work_packages/351 |
| `WI-B4a-02` |  | `WI-B4a-02-baseline-limits-audit.md` | Audit WorldBuilder large-map limits | https://projects.reid-corp.com/work_packages/352 |
| `WI-B4a-03` |  | `WI-B4a-03-region-delta-contract.md` | Add the WorldBuilder region-delta edit contract | https://projects.reid-corp.com/work_packages/353 |
| `WI-B4a-04` |  | `WI-B4a-04-height-brush-region-delta.md` | Remove full-grid height brush clones | https://projects.reid-corp.com/work_packages/354 |
| `WI-B4a-05` |  | `WI-B4a-05-layer-region-deltas.md` | Apply region deltas to terrain layers | https://projects.reid-corp.com/work_packages/355 |
| `WI-B4a-06` |  | `WI-B4a-06-large-map-wizard.md` | Add large-map wizard presets and 16-bit defaults | https://projects.reid-corp.com/work_packages/356 |
| `WI-B4a-07` |  | `WI-B4a-07-cdlod-streaming-design.md` | Design CDLOD terrain streaming seams | https://projects.reid-corp.com/work_packages/357 |
| `WI-B4a-08` |  | `WI-B4a-08-cdlod-selection.md` | Implement CDLOD quadtree selection | https://projects.reid-corp.com/work_packages/358 |
| `WI-B4a-09` |  | `WI-B4a-09-visible-patch-streaming.md` | Stream visible terrain patches | https://projects.reid-corp.com/work_packages/359 |
| `WI-B4a-10` |  | `WI-B4a-10-runtime-chunk-store.md` | Implement runtime terrain chunk store | https://projects.reid-corp.com/work_packages/360 |
| `WI-B4a-11` |  | `WI-B4a-11-large-map-roundtrip.md` | Validate large-map round-trip and play | https://projects.reid-corp.com/work_packages/361 |
| `WI-B4b-01` |  | `WI-B4b-01-scaffold-geospatial-project.md` | Scaffold `Panopticon.Geospatial`, diagnostics, and limits | https://projects.reid-corp.com/work_packages/362 |
| `WI-B4b-02` |  | `WI-B4b-02-import-manifest.md` | Import manifest schema and loader | https://projects.reid-corp.com/work_packages/363 |
| `WI-B4b-03` |  | `WI-B4b-03-interchange-values.md` | Geospatial interchange values and interfaces | https://projects.reid-corp.com/work_packages/364 |
| `WI-B4b-04` |  | `WI-B4b-04-transverse-mercator.md` | WGS84 Transverse Mercator projection | https://projects.reid-corp.com/work_packages/365 |
| `WI-B4b-05` |  | `WI-B4b-05-coordinate-transformer.md` | Coordinate transformer for supported CRS | https://projects.reid-corp.com/work_packages/366 |
| `WI-B4b-06` |  | `WI-B4b-06-local-map-grid.md` | Local map grid and distortion check | https://projects.reid-corp.com/work_packages/367 |
| `WI-B4b-07` |  | `WI-B4b-07-kml-reader.md` | Bounded KML reader | https://projects.reid-corp.com/work_packages/368 |
| `WI-B4b-08` |  | `WI-B4b-08-kmz-archive.md` | Safe KMZ archive reader | https://projects.reid-corp.com/work_packages/369 |
| `WI-B4b-09` |  | `WI-B4b-09-geotiff-fixtures.md` | Synthetic GeoTIFF fixture writer | https://projects.reid-corp.com/work_packages/370 |
| `WI-B4b-10` |  | `WI-B4b-10-geotiff-reader.md` | GeoTIFF raster reader | https://projects.reid-corp.com/work_packages/371 |
| `WI-B4b-11` |  | `WI-B4b-11-raster-sampling.md` | Deterministic raster sampling kernels | https://projects.reid-corp.com/work_packages/372 |
| `WI-B4b-12` |  | `WI-B4b-12-terrain-chunks.md` | Terrain chunk generator | https://projects.reid-corp.com/work_packages/373 |
| `WI-B4b-13` |  | `WI-B4b-13-imagery-chunks.md` | Imagery chunk generator | https://projects.reid-corp.com/work_packages/374 |
| `WI-B4b-14` |  | `WI-B4b-14-material-layer.md` | Material layer generator | https://projects.reid-corp.com/work_packages/375 |
| `WI-B4b-15` |  | `WI-B4b-15-feature-clipping.md` | Feature transform and clipping | https://projects.reid-corp.com/work_packages/376 |
| `WI-B4b-16` |  | `WI-B4b-16-feature-mapping.md` | Declarative feature mapping | https://projects.reid-corp.com/work_packages/377 |
| `WI-B4b-17` |  | `WI-B4b-17-terrain-masks.md` | Passability and buildability masks | https://projects.reid-corp.com/work_packages/378 |
| `WI-B4b-18` |  | `WI-B4b-18-import-pipeline-payload.md` | Import pipeline and standalone payload | https://projects.reid-corp.com/work_packages/379 |
| `WI-B4b-19` |  | `WI-B4b-19-geoimport-cli.md` | GeoImport command-line tool | https://projects.reid-corp.com/work_packages/380 |
| `WI-B4b-20` |  | `WI-B4b-20-control-point-validation.md` | Control-point validation | https://projects.reid-corp.com/work_packages/381 |
| `WI-B4b-21` |  | `WI-B4b-21-audit-playable-mapfile.md` | Audit the minimum playable legacy MapFile | https://projects.reid-corp.com/work_packages/382 |
| `WI-B4b-22` |  | `WI-B4b-22-mapfile-defaults.md` | Legacy MapFile default factories | https://projects.reid-corp.com/work_packages/383 |
| `WI-B4b-23` |  | `WI-B4b-23-payload-to-mapfile.md` | Payload to legacy MapFile preview builder | https://projects.reid-corp.com/work_packages/384 |
| `WI-B4b-24` |  | `WI-B4b-24-preview-evidence.md` | Dev preview CLI verb and play evidence | https://projects.reid-corp.com/work_packages/385 |
| `WI-B5-01` |  | `WI-B5-01-render-scale-probe-baseline.md` | Render-path counters and render-scale probe (baseline) | https://projects.reid-corp.com/work_packages/386 |
| `WI-B5-02` |  | `WI-B5-02-gpu-instancing-repeated-models.md` | Source-agnostic GPU instancing for repeated models | https://projects.reid-corp.com/work_packages/387 |
| `WI-B5-03` |  | `WI-B5-03-distance-lod-gate-and-sort-once.md` | Distance LOD gate and sort-once culling | https://projects.reid-corp.com/work_packages/388 |
| `WI-B5-04` |  | `WI-B5-04-render-bucket-shadow-submit.md` | Render-bucket/shadow submit improvements | https://projects.reid-corp.com/work_packages/389 |
| `WI-B5-05` |  | `WI-B5-05-anchor-budget-verification.md` | Scale-anchor budget verification and evidence | https://projects.reid-corp.com/work_packages/390 |
| `WI-B6-01` |  | `WI-B6-01-partition-scheme-contract.md` | Design the partition scheme contract | https://projects.reid-corp.com/work_packages/391 |
| `WI-B6-02` |  | `WI-B6-02-halo-derivation-contract.md` | Design halo derivation rules | https://projects.reid-corp.com/work_packages/392 |
| `WI-B6-03` |  | `WI-B6-03-boundary-message-format.md` | Design the boundary message format | https://projects.reid-corp.com/work_packages/393 |
| `WI-B6-04` |  | `WI-B6-04-western-front-front-strip-partition.md` | Implement B6 front-strip partition for W | https://projects.reid-corp.com/work_packages/394 |
| `WI-B6-05` |  | `WI-B6-05-inprocess-shard-coordinator.md` | Implement in-process shard coordinator | https://projects.reid-corp.com/work_packages/395 |
| `WI-B6-06` |  | `WI-B6-06-json-boundary-transport.md` | Implement JSON boundary transport | https://projects.reid-corp.com/work_packages/396 |
| `WI-B6-07` |  | `WI-B6-07-western-front-shard-evidence.md` | Run B6-2 Western Front shard evidence | https://projects.reid-corp.com/work_packages/397 |
| `WI-B6-08` |  | `WI-B6-08-compact-boundary-codec.md` | Implement compact boundary codec | https://projects.reid-corp.com/work_packages/398 |
| `WI-B6-09` |  | `WI-B6-09-multiprocess-shard-transport.md` | Implement multi-process shard transport | https://projects.reid-corp.com/work_packages/399 |
| `WI-B6-10` |  | `WI-B6-10-transport-fault-diagnostics.md` | Add transport fault diagnostics | https://projects.reid-corp.com/work_packages/400 |
| `WI-B7-01` |  | `WI-B7-01-fidelity-model-registry.md` | Add the fidelity model and registry | https://projects.reid-corp.com/work_packages/401 |
| `WI-B7-02` |  | `WI-B7-02-replicated-policy-input-contract.md` | Define replicated fidelity policy inputs | https://projects.reid-corp.com/work_packages/402 |
| `WI-B7-03` |  | `WI-B7-03-registry-domain-projection.md` | Project fidelity into domain state and metrics | https://projects.reid-corp.com/work_packages/403 |
| `WI-B7-04` |  | `WI-B7-04-group-aggregation-contract.md` | Generalize W2 group aggregation into a reusable contract | https://projects.reid-corp.com/work_packages/404 |
| `WI-B7-05` |  | `WI-B7-05-group-aggregation-error-hooks.md` | Add aggregation error hooks | https://projects.reid-corp.com/work_packages/405 |
| `WI-B7-06` |  | `WI-B7-06-fidelity-visualization-bridge.md` | Bridge fidelity states into A5 visualization | https://projects.reid-corp.com/work_packages/406 |
| `WI-B7-07` |  | `WI-B7-07-fidelity-error-report.md` | Report fidelity transitions and error | https://projects.reid-corp.com/work_packages/407 |
| `WI-C1-01` |  | `WI-C1-01-audit-model-import-path.md` | Audit the W3D model import path | https://projects.reid-corp.com/work_packages/408 |
| `WI-C1-02` |  | `WI-C1-02-audit-animation-import-path.md` | Audit the W3D animation import path | https://projects.reid-corp.com/work_packages/409 |
| `WI-C1-03` |  | `WI-C1-03-deep-dive-contract-design.md` | Write the C1 deep dive and contract design | https://projects.reid-corp.com/work_packages/410 |
| `WI-C1-04` |  | `WI-C1-04-import-interchange-types.md` | Add CPU-only model and animation interchange types | https://projects.reid-corp.com/work_packages/411 |
| `WI-C1-05` |  | `WI-C1-05-importer-contracts.md` | Add model and animation importer contracts | https://projects.reid-corp.com/work_packages/412 |
| `WI-C1-06` |  | `WI-C1-06-w3d-animation-importer.md` | Extract W3D animation parsing | https://projects.reid-corp.com/work_packages/413 |
| `WI-C1-07` |  | `WI-C1-07-neutral-animation-runtime.md` | Materialize neutral animations | https://projects.reid-corp.com/work_packages/414 |
| `WI-C1-08` |  | `WI-C1-08-w3d-material-conversion.md` | Extract W3D material conversion | https://projects.reid-corp.com/work_packages/415 |
| `WI-C1-09` |  | `WI-C1-09-w3d-mesh-conversion.md` | Extract W3D mesh conversion | https://projects.reid-corp.com/work_packages/416 |
| `WI-C1-10` |  | `WI-C1-10-w3d-model-importer.md` | Compose models through the W3D importer | https://projects.reid-corp.com/work_packages/417 |
| `WI-C1-11` |  | `WI-C1-11-model-materializer.md` | Materialize imported models | https://projects.reid-corp.com/work_packages/418 |
| `WI-C1-12` |  | `WI-C1-12-w3d-parity-tests.md` | Lock W3D import parity | https://projects.reid-corp.com/work_packages/419 |
| `WI-C1-13` |  | `WI-C1-13-wire-importers.md` | Switch loaders and shipped games to importers | https://projects.reid-corp.com/work_packages/420 |
| `WI-C1-14` |  | `WI-C1-14-remove-legacy-conversion.md` | Remove legacy W3D runtime conversion | https://projects.reid-corp.com/work_packages/421 |
| `WI-C10a-01` |  | `WI-C10a-01-bar-archive-file-system.md` | BAR archive reader, LZ4, and `BarFileSystem` | https://projects.reid-corp.com/work_packages/422 |
| `WI-C10a-02` |  | `WI-C10a-02-xmb-binary-xml-decoder.md` | XMB binary XML decoder | https://projects.reid-corp.com/work_packages/423 |
| `WI-C10a-03` |  | `WI-C10a-03-ddt-texture-decode.md` | DDT reader and image-decoder branch | https://projects.reid-corp.com/work_packages/424 |
| `WI-C10b-01` |  | `WI-C10b-01-granny-container-reader.md` | Generic Granny container reader | https://projects.reid-corp.com/work_packages/425 |
| `WI-C10b-02` |  | `WI-C10b-02-granny-static-model-import.md` | Granny static model and skeleton import | https://projects.reid-corp.com/work_packages/426 |
| `WI-C10b-03` |  | `WI-C10b-03-aoe3-material-mapping.md` | `.material.XMB` mapping onto C3 PBR | https://projects.reid-corp.com/work_packages/427 |
| `WI-C10b-04` |  | `WI-C10b-04-aoe3-static-proof.md` | AoE3 static proof capture | https://projects.reid-corp.com/work_packages/428 |
| `WI-C10c-01` |  | `WI-C10c-01-granny-skin-import.md` | Granny bone bindings as four-influence skins | https://projects.reid-corp.com/work_packages/429 |
| `WI-C10c-02` |  | `WI-C10c-02-granny-animation-import.md` | Granny float-curve clips as C1 TRS channels | https://projects.reid-corp.com/work_packages/430 |
| `WI-C10c-03` |  | `WI-C10c-03-aoe3-animfile-resolver.md` | Animfile resolver: models, variants, clips, attachments | https://projects.reid-corp.com/work_packages/431 |
| `WI-C10c-04` |  | `WI-C10c-04-aoe3-animated-proof.md` | AoE3 animated proof capture | https://projects.reid-corp.com/work_packages/432 |
| `WI-C10d-01` |  | `WI-C10d-01-decompressor-provenance.md` | Decompressor provenance gate | https://projects.reid-corp.com/work_packages/433 |
| `WI-C10d-02` |  | `WI-C10d-02-managed-section-codecs.md` | Managed section codecs | https://projects.reid-corp.com/work_packages/434 |
| `WI-C10d-03` |  | `WI-C10d-03-compressed-reader-integration.md` | Compressed reader integration | https://projects.reid-corp.com/work_packages/435 |
| `WI-C10d-04` |  | `WI-C10d-04-compressed-model-proof.md` | Compressed model proof | https://projects.reid-corp.com/work_packages/436 |
| `WI-C12a-01` |  | `WI-C12a-01-pack-file-system.md` | `PFHs` pack reader, LZ4, and `PackFileSystem` | https://projects.reid-corp.com/work_packages/437 |
| `WI-C12a-02` |  | `WI-C12a-02-db-table-reader.md` | DB table reader and unit-binding columns | https://projects.reid-corp.com/work_packages/438 |
| `WI-C12b-01` |  | `WI-C12b-01-mesh-layout-investigation.md` | RMV2 and `VMPF` mesh layout investigation | https://projects.reid-corp.com/work_packages/439 |
| `WI-C12b-02` |  | `WI-C12b-02-dds-dx10-bc7-decode.md` | DDS `DX10` header, BC7, and imported DDS decode | https://projects.reid-corp.com/work_packages/440 |
| `WI-C12b-03` |  | `WI-C12b-03-rmv2-static-import.md` | `Rmv2File` reader and static `RMV2` import | https://projects.reid-corp.com/work_packages/441 |
| `WI-C12b-04` |  | `WI-C12b-04-part-import-and-materials.md` | `VMPF` parts and Complete Edition materials | https://projects.reid-corp.com/work_packages/442 |
| `WI-C12b-05` |  | `WI-C12b-05-static-proof.md` | SHOGUN 2 static proof capture | https://projects.reid-corp.com/work_packages/443 |
| `WI-C12c-01` |  | `WI-C12c-01-anim-frame-investigation.md` | Version 1 `.anim` frame data investigation | https://projects.reid-corp.com/work_packages/444 |
| `WI-C12c-02` |  | `WI-C12c-02-anim-import.md` | `AnimFile` reader, skeletons, and clip import | https://projects.reid-corp.com/work_packages/445 |
| `WI-C12c-03` |  | `WI-C12c-03-part-skin-import.md` | `VMPF` skin import | https://projects.reid-corp.com/work_packages/446 |
| `WI-C12c-04` |  | `WI-C12c-04-unit-variant-composer.md` | `.unit_variant` reader and seeded composer | https://projects.reid-corp.com/work_packages/447 |
| `WI-C12c-05` |  | `WI-C12c-05-animation-table-resolver.md` | Animation tables, fragments, and unit resolution | https://projects.reid-corp.com/work_packages/448 |
| `WI-C12c-06` |  | `WI-C12c-06-animated-proof.md` | SHOGUN 2 animated squad proof | https://projects.reid-corp.com/work_packages/449 |
| `WI-C2-01` |  | `WI-C2-01-add-imported-animation-targets.md` | Add imported animation targets | https://projects.reid-corp.com/work_packages/450 |
| `WI-C2-02` |  | `WI-C2-02-add-runtime-animation-binding.md` | Add runtime animation binding | https://projects.reid-corp.com/work_packages/451 |
| `WI-C2-03` |  | `WI-C2-03-bind-named-animation-targets.md` | Bind named animation targets | https://projects.reid-corp.com/work_packages/452 |
| `WI-C2-04` |  | `WI-C2-04-cache-bound-animations.md` | Cache bound animations | https://projects.reid-corp.com/work_packages/453 |
| `WI-C2-05` |  | `WI-C2-05-route-gameplay-animation-binding.md` | Route gameplay animation binding | https://projects.reid-corp.com/work_packages/454 |
| `WI-C2-06` |  | `WI-C2-06-lock-retargeting-acceptance.md` | Lock retargeting acceptance | https://projects.reid-corp.com/work_packages/455 |
| `WI-C3-01` |  | `WI-C3-01-add-pbr-interchange-contracts.md` | Add PBR interchange contracts | https://projects.reid-corp.com/work_packages/456 |
| `WI-C3-02` |  | `WI-C3-02-validate-pbr-import-graphs.md` | Validate PBR import graphs | https://projects.reid-corp.com/work_packages/457 |
| `WI-C3-03` |  | `WI-C3-03-generate-imported-tangents.md` | Generate imported tangents | https://projects.reid-corp.com/work_packages/458 |
| `WI-C3-04` |  | `WI-C3-04-decode-imported-images.md` | Decode imported images | https://projects.reid-corp.com/work_packages/459 |
| `WI-C3-05` |  | `WI-C3-05-materialize-imported-textures.md` | Materialize imported textures | https://projects.reid-corp.com/work_packages/460 |
| `WI-C3-06` |  | `WI-C3-06-add-core-pbr-shader.md` | Add the core PBR shader scaffold | https://projects.reid-corp.com/work_packages/461 |
| `WI-C3-07` |  | `WI-C3-07-implement-core-pbr-lighting.md` | Implement core PBR lighting | https://projects.reid-corp.com/work_packages/462 |
| `WI-C3-08` |  | `WI-C3-08-cache-pbr-material-states.md` | Cache PBR material states | https://projects.reid-corp.com/work_packages/463 |
| `WI-C3-09` |  | `WI-C3-09-wire-pbr-model-materialization.md` | Wire PBR model materialization | https://projects.reid-corp.com/work_packages/464 |
| `WI-C3-10` |  | `WI-C3-10-test-pbr-materialization.md` | Test PBR materialization | https://projects.reid-corp.com/work_packages/465 |
| `WI-C3-11` |  | `WI-C3-11-capture-pbr-feature-matrix.md` | Capture the PBR feature matrix | https://projects.reid-corp.com/work_packages/466 |
| `WI-C4-01` |  | `WI-C4-01-bootstrap-gltf-import.md` | Bootstrap glTF import | https://projects.reid-corp.com/work_packages/467 |
| `WI-C4-02` |  | `WI-C4-02-resolve-gltf-resources.md` | Resolve glTF resources | https://projects.reid-corp.com/work_packages/468 |
| `WI-C4-03` |  | `WI-C4-03-add-inverse-bind-matrices.md` | Add inverse-bind matrices | https://projects.reid-corp.com/work_packages/469 |
| `WI-C4-04` |  | `WI-C4-04-add-animation-interpolation.md` | Add animation interpolation | https://projects.reid-corp.com/work_packages/470 |
| `WI-C4-05` |  | `WI-C4-05-convert-gltf-scene-nodes.md` | Convert glTF scene nodes | https://projects.reid-corp.com/work_packages/471 |
| `WI-C4-06` |  | `WI-C4-06-decode-gltf-primitives.md` | Decode glTF primitives | https://projects.reid-corp.com/work_packages/472 |
| `WI-C4-07` |  | `WI-C4-07-convert-gltf-skinning.md` | Convert glTF skinning | https://projects.reid-corp.com/work_packages/473 |
| `WI-C4-08` |  | `WI-C4-08-convert-gltf-animations.md` | Convert glTF animations | https://projects.reid-corp.com/work_packages/474 |
| `WI-C4-09` |  | `WI-C4-09-map-gltf-pbr-materials.md` | Map glTF PBR materials | https://projects.reid-corp.com/work_packages/475 |
| `WI-C4-10` |  | `WI-C4-10-enforce-gltf-import-policy.md` | Enforce glTF import policy | https://projects.reid-corp.com/work_packages/476 |
| `WI-C4-11` |  | `WI-C4-11-add-gltf-fixture-matrix.md` | Add the glTF fixture matrix | https://projects.reid-corp.com/work_packages/477 |
| `WI-C4-12` |  | `WI-C4-12-capture-gltf-runtime-proof.md` | Capture glTF runtime proof | https://projects.reid-corp.com/work_packages/478 |
| `WI-C4a-01` |  | `WI-C4a-01-inventory-proof-models.md` | Inventory the EE2 proof models | https://projects.reid-corp.com/work_packages/479 |
| `WI-C4a-02` |  | `WI-C4a-02-add-nif-container-reader.md` | Add the NIF 10.1 container reader | https://projects.reid-corp.com/work_packages/480 |
| `WI-C4a-03` |  | `WI-C4a-03-decode-nif-scene-graph.md` | Decode the NIF scene graph | https://projects.reid-corp.com/work_packages/481 |
| `WI-C4a-04` |  | `WI-C4a-04-decode-nif-triangle-geometry.md` | Decode NIF triangle geometry | https://projects.reid-corp.com/work_packages/482 |
| `WI-C4a-05` |  | `WI-C4a-05-decode-nif-material-properties.md` | Decode NIF material properties | https://projects.reid-corp.com/work_packages/483 |
| `WI-C4a-06` |  | `WI-C4a-06-select-nif-lod.md` | Select the highest NIF LOD | https://projects.reid-corp.com/work_packages/484 |
| `WI-C4a-07` |  | `WI-C4a-07-add-nif-model-importer.md` | Add the NIF model importer | https://projects.reid-corp.com/work_packages/485 |
| `WI-C4a-08` |  | `WI-C4a-08-add-zip-file-system.md` | Add a ZIP-backed file system | https://projects.reid-corp.com/work_packages/486 |
| `WI-C4a-09` |  | `WI-C4a-09-verify-ee2-import-descriptors.md` | Verify the EE2 import descriptors | https://projects.reid-corp.com/work_packages/487 |
| `WI-C4a-10` |  | `WI-C4a-10-add-headless-model-capture.md` | Add headless runtime-model capture | https://projects.reid-corp.com/work_packages/488 |
| `WI-C4a-11` |  | `WI-C4a-11-capture-ee2-proof-models.md` | Capture the EE2 proof models | https://projects.reid-corp.com/work_packages/489 |
| `WI-C4b-01` |  | `WI-C4b-01-decode-nif-pixel-data.md` | Decode NiPixelData texcache containers | https://projects.reid-corp.com/work_packages/490 |
| `WI-C4b-02` |  | `WI-C4b-02-render-textured-proof-models.md` | Render the C4a proof models textured | https://projects.reid-corp.com/work_packages/491 |
| `WI-C4c-01` |  | `WI-C4c-01-decode-kf-animation-blocks.md` | Decode KF animation blocks | https://projects.reid-corp.com/work_packages/492 |
| `WI-C4c-02` |  | `WI-C4c-02-preserve-nif-node-hierarchy.md` | Preserve the NIF node hierarchy as bones | https://projects.reid-corp.com/work_packages/493 |
| `WI-C4c-03` |  | `WI-C4c-03-evaluate-nif-key-curves.md` | Evaluate NIF key curves | https://projects.reid-corp.com/work_packages/494 |
| `WI-C4c-04` |  | `WI-C4c-04-add-nif-animation-importer.md` | Add the NIF KF animation importer | https://projects.reid-corp.com/work_packages/495 |
| `WI-C4c-05` |  | `WI-C4c-05-verify-ee2-clip-descriptors.md` | Verify EE2 clip descriptors | https://projects.reid-corp.com/work_packages/496 |
| `WI-C4c-06` |  | `WI-C4c-06-capture-animated-proof-models.md` | Capture the animated proof models | https://projects.reid-corp.com/work_packages/497 |
| `WI-C4d-01` |  | `WI-C4d-01-fix-nif-rotation-convention.md` | Fix the NIF rotation convention | https://projects.reid-corp.com/work_packages/498 |
| `WI-C4d-02` |  | `WI-C4d-02-decode-nif-skin-blocks.md` | Decode NIF skin blocks | https://projects.reid-corp.com/work_packages/499 |
| `WI-C4d-03` |  | `WI-C4d-03-widen-c1-skin-influences.md` | Widen C1 skin influences to four | https://projects.reid-corp.com/work_packages/500 |
| `WI-C4d-04` |  | `WI-C4d-04-widen-runtime-skin-influences.md` | Widen runtime skinning to four influences | https://projects.reid-corp.com/work_packages/501 |
| `WI-C4d-05` |  | `WI-C4d-05-accept-gltf-four-influences.md` | Accept four glTF influences | https://projects.reid-corp.com/work_packages/502 |
| `WI-C4d-06` |  | `WI-C4d-06-convert-nif-skinned-shapes.md` | Convert NIF skinned shapes | https://projects.reid-corp.com/work_packages/503 |
| `WI-C4d-07` |  | `WI-C4d-07-verify-ee2-skinned-proof.md` | Verify the EE2 skinned proof | https://projects.reid-corp.com/work_packages/504 |
| `WI-C4e-01` |  | `WI-C4e-01-decode-ee2-property-controllers.md` | Decode EE2 property controllers | https://projects.reid-corp.com/work_packages/505 |
| `WI-C4e-02` |  | `WI-C4e-02-resolve-ee2-property-targets.md` | Resolve EE2 property targets | https://projects.reid-corp.com/work_packages/506 |
| `WI-C4e-03` |  | `WI-C4e-03-extend-c1-property-channels.md` | Extend C1 property channels | https://projects.reid-corp.com/work_packages/507 |
| `WI-C4e-04` |  | `WI-C4e-04-convert-ee2-property-animation.md` | Convert EE2 property animation | https://projects.reid-corp.com/work_packages/508 |
| `WI-C4e-05` |  | `WI-C4e-05-add-runtime-material-animation.md` | Add runtime material animation | https://projects.reid-corp.com/work_packages/509 |
| `WI-C4e-06` |  | `WI-C4e-06-verify-ee2-property-proofs.md` | Verify EE2 property proofs | https://projects.reid-corp.com/work_packages/510 |
| `WI-C4f-01` |  | `WI-C4f-01-add-scale-to-c1-runtime.md` | Add scale to C1 and runtime animation | https://projects.reid-corp.com/work_packages/511 |
| `WI-C4f-02` |  | `WI-C4f-02-preserve-nif-rest-scale.md` | Preserve NIF rest scale | https://projects.reid-corp.com/work_packages/512 |
| `WI-C4f-03` |  | `WI-C4f-03-convert-kf-scale-keys.md` | Convert KF scale keys | https://projects.reid-corp.com/work_packages/513 |
| `WI-C4f-04` |  | `WI-C4f-04-validate-scale-normals-bounds.md` | Validate scale normals and bounds | https://projects.reid-corp.com/work_packages/514 |
| `WI-C4f-05` |  | `WI-C4f-05-verify-ee2-scale-descriptors.md` | Verify EE2 scale descriptors | https://projects.reid-corp.com/work_packages/515 |
| `WI-C4f-06` |  | `WI-C4f-06-capture-ee2-scale-proof.md` | Capture EE2 scale proof | https://projects.reid-corp.com/work_packages/516 |
| `WI-C4g-01` |  | `WI-C4g-01-decode-nif-morph-blocks.md` | Decode NIF morph blocks | https://projects.reid-corp.com/work_packages/517 |
| `WI-C4g-02` |  | `WI-C4g-02-add-morph-interchange-gate.md` | Add morph interchange gate | https://projects.reid-corp.com/work_packages/518 |
| `WI-C4g-03` |  | `WI-C4g-03-import-nif-morph-targets.md` | Import NIF morph targets | https://projects.reid-corp.com/work_packages/519 |
| `WI-C4g-04` |  | `WI-C4g-04-materialize-cpu-morph-runtime.md` | Materialize CPU morph runtime | https://projects.reid-corp.com/work_packages/520 |
| `WI-C4g-05` |  | `WI-C4g-05-bind-morph-weight-animation.md` | Bind morph weight animation | https://projects.reid-corp.com/work_packages/521 |
| `WI-C4g-06` |  | `WI-C4g-06-capture-ee2-morph-proofs.md` | Capture EE2 morph proofs | https://projects.reid-corp.com/work_packages/522 |
| `WI-C4h-01` |  | `WI-C4h-01-census-ee2-particle-effects.md` | Census EE2 particle effects | https://projects.reid-corp.com/work_packages/523 |
| `WI-C4h-02` |  | `WI-C4h-02-decode-legacy-particle-blocks.md` | Decode legacy particle blocks | https://projects.reid-corp.com/work_packages/524 |
| `WI-C4h-03` |  | `WI-C4h-03-add-particle-interchange.md` | Add particle interchange | https://projects.reid-corp.com/work_packages/525 |
| `WI-C4h-04` |  | `WI-C4h-04-translate-particles-to-runtime-templates.md` | Translate particles to runtime templates | https://projects.reid-corp.com/work_packages/526 |
| `WI-C4h-05` |  | `WI-C4h-05-bind-effect-attachments.md` | Bind effect attachments | https://projects.reid-corp.com/work_packages/527 |
| `WI-C4h-06` |  | `WI-C4h-06-capture-particle-proof-effects.md` | Capture particle proof effects | https://projects.reid-corp.com/work_packages/528 |
| `WI-C4i-01` |  | `WI-C4i-01-add-legacy-kf-header-layouts.md` | Add legacy KF header layouts | https://projects.reid-corp.com/work_packages/529 |
| `WI-C4i-02` |  | `WI-C4i-02-reuse-kf-block-readers-for-legacy.md` | Reuse KF block readers for legacy | https://projects.reid-corp.com/work_packages/530 |
| `WI-C4i-03` |  | `WI-C4i-03-normalize-legacy-kf-semantics.md` | Normalize legacy KF semantics | https://projects.reid-corp.com/work_packages/531 |
| `WI-C4i-04` |  | `WI-C4i-04-bind-ddf-legacy-proof-clips.md` | Bind DDF legacy proof clips | https://projects.reid-corp.com/work_packages/532 |
| `WI-C4i-05` |  | `WI-C4i-05-lock-legacy-family-rejections.md` | Lock legacy family rejections | https://projects.reid-corp.com/work_packages/533 |
| `WI-C4i-06` |  | `WI-C4i-06-capture-legacy-rigid-proofs.md` | Capture legacy rigid proofs | https://projects.reid-corp.com/work_packages/534 |
| `WI-C5-01` |  | `WI-C5-01-add-external-inspection-facade.md` | Add external inspection facade | https://projects.reid-corp.com/work_packages/535 |
| `WI-C5-02` |  | `WI-C5-02-mount-external-source-roots.md` | Mount external source roots | https://projects.reid-corp.com/work_packages/536 |
| `WI-C5-03` |  | `WI-C5-03-add-diagnostics-and-reports.md` | Add diagnostics and reports | https://projects.reid-corp.com/work_packages/537 |
| `WI-C5-04` |  | `WI-C5-04-implement-external-model-session.md` | Implement external model session | https://projects.reid-corp.com/work_packages/538 |
| `WI-C5-05` |  | `WI-C5-05-pair-external-animations.md` | Pair external animations | https://projects.reid-corp.com/work_packages/539 |
| `WI-C5-06` |  | `WI-C5-06-add-external-model-protocol.md` | Add external model protocol | https://projects.reid-corp.com/work_packages/540 |
| `WI-C5-07` |  | `WI-C5-07-host-external-model-requests.md` | Host external model requests | https://projects.reid-corp.com/work_packages/541 |
| `WI-C5-08` |  | `WI-C5-08-render-external-model-scenes.md` | Render external model scenes | https://projects.reid-corp.com/work_packages/542 |
| `WI-C5-09` |  | `WI-C5-09-launch-testharness-preview.md` | Launch TestHarness preview | https://projects.reid-corp.com/work_packages/543 |
| `WI-C5-10` |  | `WI-C5-10-add-external-model-viewmodel.md` | Add external model view model | https://projects.reid-corp.com/work_packages/544 |
| `WI-C5-11` |  | `WI-C5-11-add-replaysketch-model-tab.md` | Add ReplaySketch Model tab | https://projects.reid-corp.com/work_packages/545 |
| `WI-C5-12` |  | `WI-C5-12-verify-external-model-tooling.md` | Verify external model tooling | https://projects.reid-corp.com/work_packages/546 |
| `WI-C7a-01` |  | `WI-C7a-01-read-casc-build-info-and-index.md` | Read CASC build info and index files | https://projects.reid-corp.com/work_packages/547 |
| `WI-C7a-02` |  | `WI-C7a-02-decode-blte-and-encoding.md` | Decode BLTE spans and the encoding file | https://projects.reid-corp.com/work_packages/548 |
| `WI-C7a-03` |  | `WI-C7a-03-restore-mndx-names.md` | Restore MNDX root names | https://projects.reid-corp.com/work_packages/549 |
| `WI-C7a-04` |  | `WI-C7a-04-expose-casc-file-system.md` | Expose CASC storage as a file system | https://projects.reid-corp.com/work_packages/550 |
| `WI-C7b-01` |  | `WI-C7b-01-read-m3-container.md` | Read the M3 container and model tags | https://projects.reid-corp.com/work_packages/551 |
| `WI-C7b-02` |  | `WI-C7b-02-convert-m3-geometry.md` | Convert M3 geometry to C1 sub-objects | https://projects.reid-corp.com/work_packages/552 |
| `WI-C7b-03` |  | `WI-C7b-03-decode-dds-texture-rules.md` | Decode DDS and derive C3 texture images | https://projects.reid-corp.com/work_packages/553 |
| `WI-C7b-04` |  | `WI-C7b-04-convert-m3-materials.md` | Convert M3 materials onto C3 PBR | https://projects.reid-corp.com/work_packages/554 |
| `WI-C7b-05` |  | `WI-C7b-05-capture-static-sc2-proof.md` | Capture the static SC2 proof models | https://projects.reid-corp.com/work_packages/555 |
| `WI-C7c-01` |  | `WI-C7c-01-add-per-axis-scale-and-absolute-clips.md` | Add per-axis scale and absolute local clips | https://projects.reid-corp.com/work_packages/556 |
| `WI-C7c-02` |  | `WI-C7c-02-convert-m3-skins.md` | Convert M3 skins | https://projects.reid-corp.com/work_packages/557 |
| `WI-C7c-03` |  | `WI-C7c-03-convert-m3-sequences.md` | Convert embedded M3 sequences | https://projects.reid-corp.com/work_packages/558 |
| `WI-C7c-04` |  | `WI-C7c-04-bind-external-m3a-clips.md` | Bind external .m3a clips | https://projects.reid-corp.com/work_packages/559 |
| `WI-C7c-05` |  | `WI-C7c-05-capture-animated-sc2-proof.md` | Capture the animated SC2 proof clips | https://projects.reid-corp.com/work_packages/560 |
| `WI-C8a-01` |  | `WI-C8a-01-match-launcher-executables-case-insensitively.md` | Match launcher executables case-insensitively | https://projects.reid-corp.com/work_packages/561 |
| `WI-C8a-02` |  | `WI-C8a-02-skip-unknown-w3d-chunks-with-diagnostics.md` | Skip unknown W3D chunks with diagnostics | https://projects.reid-corp.com/work_packages/562 |
| `WI-C8a-03` |  | `WI-C8a-03-fail-soft-w3d-importers.md` | Fail soft in the W3D importers | https://projects.reid-corp.com/work_packages/563 |
| `WI-C8a-04` |  | `WI-C8a-04-add-sage-w3d-corpus-manifest.md` | Add the SAGE W3D corpus manifest and installed corpus test | https://projects.reid-corp.com/work_packages/564 |
| `WI-C8b-01` |  | `WI-C8b-01-resolve-shader-material-families.md` | Resolve shader-material families, per-face ids, and `WsClassicEnv` | https://projects.reid-corp.com/work_packages/565 |
| `WI-C8b-02` |  | `WI-C8b-02-capture-static-bfme-proof.md` | Capture the static BFME-family proof | https://projects.reid-corp.com/work_packages/566 |
| `WI-C8b-03` |  | `WI-C8b-03-investigate-bfme-team-color.md` | Investigate the BFME-family team-color source | https://projects.reid-corp.com/work_packages/567 |
| `WI-C8b-04` |  | `WI-C8b-04-investigate-shdmesh-chunks.md` | Investigate the `SHDMESH` chunk family | https://projects.reid-corp.com/work_packages/568 |
| `WI-C8c-01` |  | `WI-C8c-01-convert-compressed-visibility-channels.md` | Convert compressed visibility bit channels | https://projects.reid-corp.com/work_packages/569 |
| `WI-C8c-02` |  | `WI-C8c-02-investigate-channel-type-15.md` | Investigate animation channel type 15 | https://projects.reid-corp.com/work_packages/570 |
| `WI-C8c-03` |  | `WI-C8c-03-verify-compressed-animation-decoding.md` | Verify adaptive-delta and motion-channel decoding across the corpus | https://projects.reid-corp.com/work_packages/571 |
| `WI-C8c-04` |  | `WI-C8c-04-capture-animated-bfme-proof.md` | Capture the animated BFME-family proof | https://projects.reid-corp.com/work_packages/572 |
| `WI-G1-01` |  | `WI-G1-01-audit-size-consumers.md` | Audit window/panel size consumers and classify points vs pixels | https://projects.reid-corp.com/work_packages/573 |
| `WI-G1-02` |  | `WI-G1-02-allow-highdpi-drawable-size.md` | Create the window with `AllowHighDpi` and expose the pixel drawable size | https://projects.reid-corp.com/work_packages/574 |
| `WI-G1-03` |  | `WI-G1-03-route-render-surfaces-to-pixels.md` | Route render surfaces and GUI layout to pixels | https://projects.reid-corp.com/work_packages/575 |
| `WI-G1-04` |  | `WI-G1-04-scale-2d-and-picking.md` | Convert mouse input to pixels at the window boundary | https://projects.reid-corp.com/work_packages/576 |
| `WI-G10-01` |  | `WI-G10-01-audit-proportions-and-reference-scale.md` | Audit proportions and reference scale | https://projects.reid-corp.com/work_packages/577 |
| `WI-G10-02` |  | `WI-G10-02-presentation-scale-overrides.md` | Presentation-only scale overrides | https://projects.reid-corp.com/work_packages/578 |
| `WI-G10-03` |  | `WI-G10-03-preset-wiring-and-golden-frames.md` | Proportions opt-in and golden frames | https://projects.reid-corp.com/work_packages/579 |
| `WI-G11-01` |  | `WI-G11-01-terrain-decal-store-and-scorch-fx.md` | Terrain decal store and scorch FX execution | https://projects.reid-corp.com/work_packages/580 |
| `WI-G11-02` |  | `WI-G11-02-terrain-decal-mesh-and-pass.md` | Terrain decal mesh and render pass | https://projects.reid-corp.com/work_packages/581 |
| `WI-G11-03` |  | `WI-G11-03-logic-and-map-scorch-producers.md` | Logic and map scorch producers | https://projects.reid-corp.com/work_packages/582 |
| `WI-G11-04` |  | `WI-G11-04-enhanced-decals-and-golden-frames.md` | Enhanced decal quality and golden frames | https://projects.reid-corp.com/work_packages/583 |
| `WI-G12-01` |  | `WI-G12-01-terrain-texture-inventory-and-map.md` | Terrain texture inventory and material map | https://projects.reid-corp.com/work_packages/584 |
| `WI-G12-02` |  | `WI-G12-02-fetch-open-materials.md` | Fetch open materials | https://projects.reid-corp.com/work_packages/585 |
| `WI-G12-03` |  | `WI-G12-03-terrain-material-pack-bake.md` | Terrain material pack bake | https://projects.reid-corp.com/work_packages/586 |
| `WI-G12-04` |  | `WI-G12-04-terrain-material-runtime-and-goldens.md` | Terrain material runtime selection and goldens | https://projects.reid-corp.com/work_packages/587 |
| `WI-G12-05` |  | `WI-G12-05-terrain-normal-roughness-shading.md` | Terrain normal and roughness shading | https://projects.reid-corp.com/work_packages/588 |
| `WI-G12-06` |  | `WI-G12-06-terrain-pbr-goldens-and-cost.md` | Terrain PBR goldens and cost | https://projects.reid-corp.com/work_packages/589 |
| `WI-G12-07` |  | `WI-G12-07-model-texture-replacement-investigation.md` | Model texture replacement (investigation) | https://projects.reid-corp.com/work_packages/590 |
| `WI-G12-08` |  | `WI-G12-08-fetch-and-bake-sky-packs.md` | Fetch and bake sky packs | https://projects.reid-corp.com/work_packages/1004 |
| `WI-G12-09` |  | `WI-G12-09-sky-pack-runtime-selection.md` | Sky pack runtime selection | https://projects.reid-corp.com/work_packages/1005 |
| `WI-G13-01` |  | `WI-G13-01-view-mode-seam-and-relative-mouse.md` | View mode seam and relative mouse | https://projects.reid-corp.com/work_packages/591 |
| `WI-G13-02` |  | `WI-G13-02-avatar-drop-mode.md` | Avatar drop mode | https://projects.reid-corp.com/work_packages/592 |
| `WI-G13-03` |  | `WI-G13-03-avatar-ground-locomotion.md` | Avatar ground locomotion | https://projects.reid-corp.com/work_packages/593 |
| `WI-G13-04` |  | `WI-G13-04-ground-level-projection-and-hud.md` | Ground-level projection, culling, and HUD | https://projects.reid-corp.com/work_packages/594 |
| `WI-G13-05` |  | `WI-G13-05-sky-and-distance-fog.md` | Skybox renderer and distance fog | https://projects.reid-corp.com/work_packages/595 |
| `WI-G13-06` |  | `WI-G13-06-transitions-goldens-and-cost.md` | Transitions, goldens, and cost | https://projects.reid-corp.com/work_packages/596 |
| `WI-G13-07` |  | `WI-G13-07-original-skybox-sources.md` | Original skybox sources | https://projects.reid-corp.com/work_packages/1003 |
| `WI-G2-01` |  | `WI-G2-01-audit-render-state-knobs.md` | Audit fixed render-state knobs and the unused LOD asset data | https://projects.reid-corp.com/work_packages/597 |
| `WI-G2-02` |  | `WI-G2-02-graphics-settings-type-and-presets.md` | Introduce the graphics settings type and the two presets | https://projects.reid-corp.com/work_packages/598 |
| `WI-G2-03` |  | `WI-G2-03-wire-settings-into-render-construction.md` | Wire the graphics settings into render resource construction | https://projects.reid-corp.com/work_packages/599 |
| `WI-G3-01` |  | `WI-G3-01-audit-frame-capture-paths.md` | Audit the frame capture paths and the golden-comparison gap | https://projects.reid-corp.com/work_packages/600 |
| `WI-G3-02` |  | `WI-G3-02-image-quality-metrics.md` | Add sharpness and edge-aliasing image metrics with unit tests | https://projects.reid-corp.com/work_packages/601 |
| `WI-G3-03` |  | `WI-G3-03-golden-frame-comparison.md` | Deterministic per-preset capture pass over the fixed scene set | https://projects.reid-corp.com/work_packages/602 |
| `WI-G3-04` |  | `WI-G3-04-golden-store-and-gate.md` | Golden store, comparer and regression gate | https://projects.reid-corp.com/work_packages/603 |
| `WI-G4-01` |  | `WI-G4-01-inventory-sample-count-sites.md` | Inventory every sample-count-bearing description | https://projects.reid-corp.com/work_packages/604 |
| `WI-G4-02` |  | `WI-G4-02-thread-sample-count-and-resolve.md` | Thread the sample count through the scene pass and add the MSAA resolve | https://projects.reid-corp.com/work_packages/605 |
| `WI-G5-01` |  | `WI-G5-01-audit-alpha-test-materials.md` | Audit alpha-tested materials and the cutout mip path | https://projects.reid-corp.com/work_packages/606 |
| `WI-G5-02` |  | `WI-G5-02-alpha-to-coverage-and-cutout-mips.md` | Alpha-to-coverage and coverage-preserving cutout mips | https://projects.reid-corp.com/work_packages/607 |
| `WI-G6-01` |  | `WI-G6-01-audit-colour-space-handling.md` | Audit colour-space handling across textures, shading, mips and output | https://projects.reid-corp.com/work_packages/608 |
| `WI-G6-02` |  | `WI-G6-02-linear-space-shading-and-dither.md` | Move shading to linear space with a dithered 8-bit output | https://projects.reid-corp.com/work_packages/609 |
| `WI-G6-03` |  | `WI-G6-03-clamp-particle-opacity.md` | Clamp particle opacity at the presentation boundary | https://projects.reid-corp.com/work_packages/610 |
| `WI-G7-01` |  | `WI-G7-01-audit-samplers-and-mip-paths.md` | Audit sampler creation, consumers, and the terrain mip path | https://projects.reid-corp.com/work_packages/611 |
| `WI-G7-02` |  | `WI-G7-02-configurable-anisotropy-and-mip-bias.md` | Configurable anisotropy, mip LOD bias, and terrain mip quality | https://projects.reid-corp.com/work_packages/612 |
| `WI-G8-01` |  | `WI-G8-01-audit-shadow-settings-flow.md` | Audit the shadow settings flow and bias application | https://projects.reid-corp.com/work_packages/613 |
| `WI-G8-02` |  | `WI-G8-02-four-cascade-tier-and-slope-scaled-bias.md` | Four-cascade shadow tier with slope-scaled bias | https://projects.reid-corp.com/work_packages/614 |
| `WI-G9-01` |  | `WI-G9-01-audit-2d-ui-resolution-path.md` | Audit the 2D and UI resolution and sampling path | https://projects.reid-corp.com/work_packages/615 |
| `WI-G9-02` |  | `WI-G9-02-native-resolution-2d-layer.md` | Improve native-pixel 2D quality with proportional layout | https://projects.reid-corp.com/work_packages/616 |
| `WI-G9-03` |  | `WI-G9-03-dynamic-hud-chrome.md` | Replace the pre-rendered HUD with dynamic drawing | https://projects.reid-corp.com/work_packages/617 |
| `WI-H1-01` |  | `WI-H1-01-map-format-audit.md` | Audit legacy map storage | https://projects.reid-corp.com/work_packages/618 |
| `WI-H1-02` |  | `WI-H1-02-format-decision-options.md` | Confirm settled H3 format choices against the audit | https://projects.reid-corp.com/work_packages/619 |
| `WI-H2-01` |  | `WI-H2-01-map-interchange-design.md` | Design map interchange for the settled package format | https://projects.reid-corp.com/work_packages/620 |
| `WI-H2-02` |  | `WI-H2-02-interchange-types.md` | Add engine-owned map interchange types | https://projects.reid-corp.com/work_packages/958 |
| `WI-H2-03` |  | `WI-H2-03-legacy-reader-seam.md` | Route the legacy map reader through the seam | https://projects.reid-corp.com/work_packages/959 |
| `WI-H2-04` |  | `WI-H2-04-runtime-bridge.md` | Bridge the runtime load path to the interchange | https://projects.reid-corp.com/work_packages/957 |
| `WI-H2a-01` |  | `WI-H2a-01-unit-loading-audit.md` | Audit unit-content loading | https://projects.reid-corp.com/work_packages/621 |
| `WI-H2a-02` |  | `WI-H2a-02-unit-import-design.md` | Ground the import contract and architecture | https://projects.reid-corp.com/work_packages/622 |
| `WI-H2a-03` |  | `WI-H2a-03-interchange-contracts.md` | Add engine-owned interchange contracts | https://projects.reid-corp.com/work_packages/623 |
| `WI-H2a-04` |  | `WI-H2a-04-inheritance-resolution.md` | Resolve imported inheritance | https://projects.reid-corp.com/work_packages/624 |
| `WI-H2a-05` |  | `WI-H2a-05-dependency-resolution.md` | Resolve typed content dependencies | https://projects.reid-corp.com/work_packages/625 |
| `WI-H2a-06` |  | `WI-H2a-06-capability-validation.md` | Validate capabilities and report failures | https://projects.reid-corp.com/work_packages/626 |
| `WI-H2a-07` |  | `WI-H2a-07-body-movement-materialization.md` | Materialize body and movement | https://projects.reid-corp.com/work_packages/627 |
| `WI-H2a-08` |  | `WI-H2a-08-weapon-armor-materialization.md` | Materialize weapons and armor | https://projects.reid-corp.com/work_packages/628 |
| `WI-H2a-09` |  | `WI-H2a-09-ability-materialization.md` | Materialize supported abilities | https://projects.reid-corp.com/work_packages/629 |
| `WI-H2a-10` |  | `WI-H2a-10-production-upgrade-materialization.md` | Materialize production and upgrades | https://projects.reid-corp.com/work_packages/630 |
| `WI-H2a-11` |  | `WI-H2a-11-visual-state-binding.md` | Bind imported visual states | https://projects.reid-corp.com/work_packages/631 |
| `WI-H2a-12` |  | `WI-H2a-12-scoped-content-publication.md` | Publish validated content scopes | https://projects.reid-corp.com/work_packages/632 |
| `WI-H2a-13` |  | `WI-H2a-13-generals-reference-adapter.md` | Route Generals through the import seam | https://projects.reid-corp.com/work_packages/633 |
| `WI-H2a-14` |  | `WI-H2a-14-generals-behavior-parity.md` | Validate Generals behavior parity | https://projects.reid-corp.com/work_packages/634 |
| `WI-H2a-15` |  | `WI-H2a-15-ee2-ddf-reader.md` | Read the bounded EE2 DDF slice | https://projects.reid-corp.com/work_packages/635 |
| `WI-H2a-16` |  | `WI-H2a-16-ee2-source-adapter.md` | Adapt EE2 to shared unit content | https://projects.reid-corp.com/work_packages/636 |
| `WI-H2a-17` |  | `WI-H2a-17-multi-source-validation.md` | Prove mixed-source units and hand off M2 | https://projects.reid-corp.com/work_packages/637 |
| `WI-H2a-18` |  | `WI-H2a-18-ee2-ground-unit-selection.md` | Select and probe the EE2 ground-combat unit | https://projects.reid-corp.com/work_packages/638 |
| `WI-H2a-19` |  | `WI-H2a-19-staged-generals-registration.md` | Stage Generals INI unit registration | https://projects.reid-corp.com/work_packages/639 |
| `WI-H2b-01` |  | `WI-H2b-01-unit-boundary-audit.md` | Audit the merged H2a unit system | https://projects.reid-corp.com/work_packages/640 |
| `WI-H2b-02` |  | `WI-H2b-02-cutover-design.md` | Design the metric cutover | https://projects.reid-corp.com/work_packages/641 |
| `WI-H2b-03` |  | `WI-H2b-03-conversion-primitives.md` | Add explicit legacy-to-metric conversion helpers | https://projects.reid-corp.com/work_packages/642 |
| `WI-H2b-04` |  | `WI-H2b-04-h2a-quantities.md` | Migrate H2a quantities and materializers to metric | https://projects.reid-corp.com/work_packages/643 |
| `WI-H2b-05` |  | `WI-H2b-05-ini-gameplay.md` | Convert gameplay INI lengths and rates at parse time | https://projects.reid-corp.com/work_packages/644 |
| `WI-H2b-06` |  | `WI-H2b-06-ini-presentation.md` | Convert presentation INI lengths and global constants | https://projects.reid-corp.com/work_packages/645 |
| `WI-H2b-07` |  | `WI-H2b-07-terrain.md` | Convert terrain spacing and elevation | https://projects.reid-corp.com/work_packages/646 |
| `WI-H2b-08` |  | `WI-H2b-08-map-placement.md` | Convert map placement and WorldBuilder adapters | https://projects.reid-corp.com/work_packages/647 |
| `WI-H2b-09` |  | `WI-H2b-09-w3d.md` | Convert W3D geometry, skeletons, and animation | https://projects.reid-corp.com/work_packages/648 |
| `WI-H2b-10` |  | `WI-H2b-10-gltf-nif.md` | Remove meters-to-feet expansion from metric-native importers | https://projects.reid-corp.com/work_packages/649 |
| `WI-H2b-11` |  | `WI-H2b-11-physics.md` | Convert physics and locomotion constants | https://projects.reid-corp.com/work_packages/650 |
| `WI-H2b-12` |  | `WI-H2b-12-spatial.md` | Convert navigation, partitioning, and script distances | https://projects.reid-corp.com/work_packages/651 |
| `WI-H2b-13` |  | `WI-H2b-13-render-audio.md` | Convert cameras, rendering distances, and audio attenuation | https://projects.reid-corp.com/work_packages/652 |
| `WI-H2b-14` |  | `WI-H2b-14-orders-replays.md` | Version orders, network payloads, and legacy replays | https://projects.reid-corp.com/work_packages/653 |
| `WI-H2b-15` |  | `WI-H2b-15-persistence.md` | Version saves, checkpoints, and recordings | https://projects.reid-corp.com/work_packages/654 |
| `WI-H2b-16` |  | `WI-H2b-16-tools-scenarios.md` | Convert tools, scenarios, and unit documentation | https://projects.reid-corp.com/work_packages/655 |
| `WI-H2b-17` |  | `WI-H2b-17-boundary-conformance.md` | Add cross-boundary conformance tests | https://projects.reid-corp.com/work_packages/656 |
| `WI-H2b-18` |  | `WI-H2b-18-release-proof.md` | Prove metric real-content parity and release | https://projects.reid-corp.com/work_packages/657 |
| `WI-H3-01` |  | `WI-H3-01-map-package-manifest-schema.md` | Define the map package manifest schema | https://projects.reid-corp.com/work_packages/658 |
| `WI-H3-02` |  | `WI-H3-02-chunk-layer-reader-writer.md` | Implement chunk layer reader and writer | https://projects.reid-corp.com/work_packages/659 |
| `WI-H3-03` |  | `WI-H3-03-legacy-to-package-importer-hooks.md` | Add legacy-to-package importer hooks | https://projects.reid-corp.com/work_packages/660 |
| `WI-H3-04` |  | `WI-H3-04-map-package-roundtrip-tests.md` | Add map package round-trip tests | https://projects.reid-corp.com/work_packages/661 |
| `WI-H4-01` |  |  | H4 schema and record-fidelity design gate | https://projects.reid-corp.com/work_packages/964 |
| `WI-H4-02` |  |  | Package reader adapter implementing IUnitSourceAdapter | https://projects.reid-corp.com/work_packages/965 |
| `WI-H4-03` |  |  | Package writer from resolved records | https://projects.reid-corp.com/work_packages/966 |
| `WI-H4-04` |  |  | Physics fields and forward compatibility | https://projects.reid-corp.com/work_packages/967 |
| `WI-H4-05` |  |  | Bounded INI parity proof | https://projects.reid-corp.com/work_packages/968 |
| `WI-H5-01` |  |  | Legacy map conversion tool | https://projects.reid-corp.com/work_packages/969 |
| `WI-H5-02` |  |  | Legacy INI conversion tool | https://projects.reid-corp.com/work_packages/970 |
| `WI-H5-03` |  |  | Round-trip validator and report | https://projects.reid-corp.com/work_packages/971 |
| `WI-H5-04` |  |  | Retained-backend integration for converted maps | https://projects.reid-corp.com/work_packages/972 |
| `WI-H5-05` |  |  | Behavioral equivalence harness | https://projects.reid-corp.com/work_packages/973 |
| `WI-H6-01` |  |  | WorldBuilder package open and save boundary conversion | https://projects.reid-corp.com/work_packages/974 |
| `WI-H6-02` |  |  | WorldBuilder package edit round trip with capture evidence | https://projects.reid-corp.com/work_packages/975 |
| `WI-H6-03` |  |  | Staged pack loading through H4 adapter and AssetStore scopes | https://projects.reid-corp.com/work_packages/976 |
| `WI-H6-04` |  |  | INI edit and reload preservation check | https://projects.reid-corp.com/work_packages/977 |
| `WI-H7-01` |  |  | Map round-trip regression suite | https://projects.reid-corp.com/work_packages/978 |
| `WI-H7-02` |  |  | Unit round-trip regression suite | https://projects.reid-corp.com/work_packages/979 |
| `WI-H7-03` |  |  | ReplaySketch new-format scenario proof | https://projects.reid-corp.com/work_packages/980 |
| `WI-H7-04` |  |  | Fault-injection catch proof | https://projects.reid-corp.com/work_packages/981 |
| `WI-M0-01` |  | `WI-M0-01-map-evaluation.md` | Official map evaluation against the battle-map requirements | https://projects.reid-corp.com/work_packages/662 |
| `WI-M0-03` |  | `WI-M0-03-deployment-zones.md` | Deployment zones and reachability check as scenario data | https://projects.reid-corp.com/work_packages/663 |
| `WI-M1-01` |  | `WI-M1-01-mass-battle-engagement-scenario.md` | 1000 vs 1000 engagement scenario | https://projects.reid-corp.com/work_packages/664 |
| `WI-M1-02` |  | `WI-M1-02-b1-capacity-measurements.md` | Capacity measurements through B1 | https://projects.reid-corp.com/work_packages/665 |
| `WI-M1-03` |  | `WI-M1-03-first-bottleneck-report.md` | First-bottleneck report | https://projects.reid-corp.com/work_packages/666 |
| `WI-M2-01` |  | `WI-M2-01-ddf-roster-resolution.md` | EE2 roster and DDF resolution | https://projects.reid-corp.com/work_packages/667 |
| `WI-M2-02` |  | `WI-M2-02-combat-aircraft-materialization.md` | EE2 combat and aircraft materialization | https://projects.reid-corp.com/work_packages/668 |
| `WI-M2-03` |  | `WI-M2-03-scoped-roster-registration.md` | Scoped EE2 roster registration | https://projects.reid-corp.com/work_packages/669 |
| `WI-M2-04` |  | `WI-M2-04-p9-contract-validation.md` | EE2 P9 contract validation | https://projects.reid-corp.com/work_packages/670 |
| `WI-M2-05` |  | `WI-M2-05-infantry-epoch15-clips.md` | Infantry epoch-15 clip import | https://projects.reid-corp.com/work_packages/671 |
| `WI-M2-06` |  | `WI-M2-06-fixed-wing-stability-and-taxi.md` | Fixed-wing flight stability and taxi | https://projects.reid-corp.com/work_packages/672 |
| `WI-M3-01` |  | `WI-M3-01-model-scale-and-factor.md` | Model scale and tiles-to-world factor | https://projects.reid-corp.com/work_packages/673 |
| `WI-M3-02` |  | `WI-M3-02-rps-damage-and-armor.md` | rps counter classes and dbcombat matrix to Generals armor and damage types | https://projects.reid-corp.com/work_packages/674 |
| `WI-M3-03` |  | `WI-M3-03-points-rosters-duels.md` | Cross-game points budget, asymmetric roster manifests, and duel matrix | https://projects.reid-corp.com/work_packages/675 |
| `WI-M4-01` |  | `WI-M4-01-army-hierarchy-scale-audit.md` | Army hierarchy scale audit | https://projects.reid-corp.com/work_packages/676 |
| `WI-M4-02` |  | `WI-M4-02-ai-decision-cost-counter.md` | AI decision cost counter | https://projects.reid-corp.com/work_packages/677 |
| `WI-M4-03` |  | `WI-M4-03-army-hierarchy-on-probe.md` | Army hierarchy on the M1 probe | https://projects.reid-corp.com/work_packages/678 |
| `WI-M4-04` |  | `WI-M4-04-squad-group-orders.md` | Squad group orders served by B4 | https://projects.reid-corp.com/work_packages/679 |
| `WI-M4-05` |  | `WI-M4-05-air-commander.md` | Air commander | https://projects.reid-corp.com/work_packages/680 |
| `WI-M4-06` |  | `WI-M4-06-ai-budget-and-stall-report.md` | AI cost budget and stall report | https://projects.reid-corp.com/work_packages/681 |
| `WI-M5-01` |  | `WI-M5-01-army-ai-side-setup.md` | Reusable army AI side setup | https://projects.reid-corp.com/work_packages/682 |
| `WI-M5-02` |  | `WI-M5-02-roster-deployment-profile.md` | Roster deployment profile | https://projects.reid-corp.com/work_packages/683 |
| `WI-M5-03` |  | `WI-M5-03-army-eliminated-outcome.md` | Army-eliminated outcome | https://projects.reid-corp.com/work_packages/684 |
| `WI-M5-04` |  | `WI-M5-04-mass-battle-scenario.md` | Mass battle scenario | https://projects.reid-corp.com/work_packages/685 |
| `WI-M5-05` |  | `WI-M5-05-kill-loss-per-unit-type.md` | Kills and losses per unit type | https://projects.reid-corp.com/work_packages/686 |
| `WI-M5-06` |  | `WI-M5-06-seeded-batch-runner.md` | Seeded batch runner | https://projects.reid-corp.com/work_packages/687 |
| `WI-M5-07` |  | `WI-M5-07-map-bias-control-and-batch-acceptance.md` | Map-bias control and batch acceptance | https://projects.reid-corp.com/work_packages/688 |
| `WI-M5-08` |  | `WI-M5-08-battle-recording-and-replay.md` | Battle recording and replay | https://projects.reid-corp.com/work_packages/689 |
| `WI-M5-09` |  | `WI-M5-09-spectator-live-view.md` | Spectator live view | https://projects.reid-corp.com/work_packages/690 |
| `WI-M5-10` |  | `WI-M5-10-live-frame-rate-measurement.md` | Live frame rate measurement | https://projects.reid-corp.com/work_packages/691 |
| `WI-MC-01` |  | `WI-MC-01-scaffold-project.md` | Scaffold the ModCollector project | https://projects.reid-corp.com/work_packages/692 |
| `WI-MC-02` |  | `WI-MC-02-manifest-and-list.md` | Manifest model and `--list` command | https://projects.reid-corp.com/work_packages/693 |
| `WI-MC-03` |  | `WI-MC-03-download-command.md` | `--download` command | https://projects.reid-corp.com/work_packages/694 |
| `WI-MC-04` |  | `WI-MC-04-extract-command.md` | `--extract` command | https://projects.reid-corp.com/work_packages/695 |
| `WI-N0-01` |  | `WI-N0-01-path-span-and-counters.md` | `path` span and request counters by origin | https://projects.reid-corp.com/work_packages/696 |
| `WI-N0-02` |  | `WI-N0-02-pathfinding-load-scenario.md` | `PathfindingLoad` scenario and baseline report | https://projects.reid-corp.com/work_packages/697 |
| `WI-N1-01` |  | `WI-N1-01-allocation-free-search.md` | Allocation-free search scratch with a path-equality corpus | https://projects.reid-corp.com/work_packages/698 |
| `WI-N1-02` |  | `WI-N1-02-region-labels.md` | Connected-region labels and unreachable early reject | https://projects.reid-corp.com/work_packages/699 |
| `WI-N1-03` |  | `WI-N1-03-passability-snapshot.md` | Versioned passability snapshot for workers | https://projects.reid-corp.com/work_packages/700 |
| `WI-N10-01` |  | `WI-N10-01-deadlock-verification.md` | Deadlock verification | https://projects.reid-corp.com/work_packages/701 |
| `WI-N11-01` |  | `WI-N11-01-passage-finder.md` | Passage finder and cross-section cache | https://projects.reid-corp.com/work_packages/702 |
| `WI-N11-02` |  | `WI-N11-02-passage-cases-and-baseline.md` | Passage cases and option-off baseline | https://projects.reid-corp.com/work_packages/703 |
| `WI-N12-01` |  | `WI-N12-01-passage-coordinator-and-queue.md` | Passage coordinator, queue order, and lane path | https://projects.reid-corp.com/work_packages/704 |
| `WI-N12-02` |  | `WI-N12-02-passage-hold-and-release-movement.md` | Passage hold, release, and route splice | https://projects.reid-corp.com/work_packages/705 |
| `WI-N13-01` |  | `WI-N13-01-opposing-flow-arbitration.md` | Opposing flow arbitration | https://projects.reid-corp.com/work_packages/706 |
| `WI-N13-02` |  | `WI-N13-02-group-passage-entry.md` | Squad and cluster passage entry | https://projects.reid-corp.com/work_packages/707 |
| `WI-N14-01` |  | `WI-N14-01-passage-verification.md` | Passage queueing verification | https://projects.reid-corp.com/work_packages/708 |
| `WI-N15-01` |  | `WI-N15-01-navigation-plan-layer-contract.md` | Navigation plan and layer contract | https://projects.reid-corp.com/work_packages/709 |
| `WI-N15-02` |  | `WI-N15-02-layered-cases-and-baseline.md` | Layered cases, layer spans, and option-off baseline | https://projects.reid-corp.com/work_packages/710 |
| `WI-N16-01` |  | `WI-N16-01-sector-graph.md` | Sector graph with incremental rebuild | https://projects.reid-corp.com/work_packages/711 |
| `WI-N16-02` |  | `WI-N16-02-strategic-planner.md` | Strategic planner and route-quality corpus | https://projects.reid-corp.com/work_packages/712 |
| `WI-N17-01` |  | `WI-N17-01-corridor-search-and-stitching.md` | Masked corridor search and segment stitching | https://projects.reid-corp.com/work_packages/713 |
| `WI-N17-02` |  | `WI-N17-02-layered-unit-main-path.md` | Layered unit main path behind `LayeredNavigation` | https://projects.reid-corp.com/work_packages/714 |
| `WI-N18-01` |  | `WI-N18-01-navigation-group-and-spine.md` | Navigation groups and spine following | https://projects.reid-corp.com/work_packages/715 |
| `WI-N18-02` |  | `WI-N18-02-width-adaptive-formation-and-passages.md` | Width-adaptive formation and passage handoff | https://projects.reid-corp.com/work_packages/716 |
| `WI-N18-03` |  | `WI-N18-03-crowd-routing.md` | Crowd routing for large selections | https://projects.reid-corp.com/work_packages/717 |
| `WI-N19-01` |  | `WI-N19-01-steering-core.md` | Reciprocal velocity-obstacle steering core | https://projects.reid-corp.com/work_packages/718 |
| `WI-N19-02` |  | `WI-N19-02-steering-integration.md` | Steering integration with N8 fallback | https://projects.reid-corp.com/work_packages/719 |
| `WI-N2-01` |  | `WI-N2-01-follower-landing.md` | Bounded landing for blocked followers | https://projects.reid-corp.com/work_packages/720 |
| `WI-N2-02` |  | `WI-N2-02-group-selection-moves.md` | Group moves for squad-less selections, behind an option | https://projects.reid-corp.com/work_packages/721 |
| `WI-N20-01` |  | `WI-N20-01-layered-navigation-verification.md` | Layered navigation verification | https://projects.reid-corp.com/work_packages/722 |
| `WI-N21-01` |  | `WI-N21-01-structure-passability-coverage.md` | Structure passability coverage | https://projects.reid-corp.com/work_packages/723 |
| `WI-N3-01` |  | `WI-N3-01-goal-field-gate.md` | Shared-destination field gate | https://projects.reid-corp.com/work_packages/724 |
| `WI-N4-01` |  | `WI-N4-01-pending-path-handles.md` | Pending path handles on `AIUpdate` | https://projects.reid-corp.com/work_packages/725 |
| `WI-N4-02` |  | `WI-N4-02-async-order-paths.md` | Async order paths behind an option | https://projects.reid-corp.com/work_packages/726 |
| `WI-N5-01` |  | `WI-N5-01-scale-verification.md` | Scale verification | https://projects.reid-corp.com/work_packages/727 |
| `WI-N6-01` |  | `WI-N6-01-movement-progress-and-stall-attribution.md` | Movement progress and stall attribution | https://projects.reid-corp.com/work_packages/728 |
| `WI-N6-02` |  | `WI-N6-02-navigation-deadlock-scenario.md` | `NavigationDeadlock` scenario and baseline | https://projects.reid-corp.com/work_packages/729 |
| `WI-N7-01` |  | `WI-N7-01-async-path-liveness.md` | Async and squad path liveness | https://projects.reid-corp.com/work_packages/730 |
| `WI-N7-02` |  | `WI-N7-02-offset-route-validation.md` | Offset route validation behind an option | https://projects.reid-corp.com/work_packages/731 |
| `WI-N8-01` |  | `WI-N8-01-blocked-wait-and-local-repath.md` | Blocked wait and bounded local repath | https://projects.reid-corp.com/work_packages/732 |
| `WI-N8-02` |  | `WI-N8-02-give-way-and-mutual-block.md` | Give-way and mutual block yield | https://projects.reid-corp.com/work_packages/733 |
| `WI-N9-01` |  | `WI-N9-01-destination-slots.md` | Destination slots behind an option | https://projects.reid-corp.com/work_packages/734 |
| `WI-N9-02` |  | `WI-N9-02-crowd-arrival.md` | Crowd arrival | https://projects.reid-corp.com/work_packages/735 |
| `WI-NUL-01` |  | `WI-NUL-01-remove-redundant-nullable-directives.md` | Remove redundant `#nullable enable` directives | https://projects.reid-corp.com/work_packages/736 |
| `WI-P0-01` |  | `WI-P0-01-catalog-replaysketch-vocabulary.md` | Catalog the current ReplaySketch command vocabulary | https://projects.reid-corp.com/work_packages/737 |
| `WI-P0-02` |  | `WI-P0-02-inventory-engine-ordertypes.md` | Inventory the engine's `OrderType` vocabulary | https://projects.reid-corp.com/work_packages/738 |
| `WI-P0-03` |  | `WI-P0-03-faction-agnostic-definition-resolution.md` | Make ReplaySketch definition resolution faction-agnostic | https://projects.reid-corp.com/work_packages/739 |
| `WI-P0-04` |  | `WI-P0-04-extract-ipreferencesstore.md` | Extract `IPreferencesStore` from ReplaySketch `MainViewModel` | https://projects.reid-corp.com/work_packages/740 |
| `WI-P0-05` |  | `WI-P0-05-inject-mainthread-and-process-seams.md` | Inject `IMainThreadDispatcher` and `IProcessLauncher` seams into `MainViewModel` | https://projects.reid-corp.com/work_packages/741 |
| `WI-P0-06` |  | `WI-P0-06-exporter-open-command-coverage.md` | Generalize ReplaySketch exporter for open `CommandSpec` commands | https://projects.reid-corp.com/work_packages/742 |
| `WI-P0-07` |  | `WI-P0-07-assertion-layer.md` | Assertion layer: data model and evaluator | https://projects.reid-corp.com/work_packages/743 |
| `WI-P0-08` |  | `WI-P0-08-run-and-assert-entry-point.md` | Headless run-and-assert entry point | https://projects.reid-corp.com/work_packages/744 |
| `WI-P0-09` |  | `WI-P0-09-large-procedural-authoring.md` | Large/procedural authoring: verify model and add scale test | https://projects.reid-corp.com/work_packages/745 |
| `WI-P0-10` |  | `WI-P0-10-multi-player-attribution-test.md` | Multi-player command attribution test | https://projects.reid-corp.com/work_packages/746 |
| `WI-P0-11` |  | `WI-P0-11-object-handle-model-types.md` | Object handle and query selector model types | https://projects.reid-corp.com/work_packages/747 |
| `WI-P0-12` |  | `WI-P0-12-runtime-handle-resolution.md` | Runtime object handle and selector resolution in `HarnessRunner` | https://projects.reid-corp.com/work_packages/748 |
| `WI-P0-13` |  | `WI-P0-13-per-ordertype-headless-coverage.md` | Per-OrderType headless coverage (move / attack / construct / produce / ability) | https://projects.reid-corp.com/work_packages/749 |
| `WI-P0-14a` | `WI-P0-14` | `WI-P0-14-harness-runner-handle-dispatch.md` | Harness runner direct-dispatch path for handle-dependent commands | https://projects.reid-corp.com/work_packages/750 |
| `WI-P0-14b` | `WI-P0-14` | `WI-P0-14-scenario-matrix-runner.md` | ScenarioMatrix model and runner (P0-5b) | https://projects.reid-corp.com/work_packages/751 |
| `WI-P0-15` |  | `WI-P0-15-wire-acceptance-test-stubs.md` | Wire P0 acceptance test stubs to real implementations | https://projects.reid-corp.com/work_packages/752 |
| `WI-P0-16` |  | `WI-P0-16-invariant-early-abort.md` | Invariant early-abort in HarnessRunner | https://projects.reid-corp.com/work_packages/753 |
| `WI-P0-17` |  | `WI-P0-17-code-defined-predicate-escape-hatch.md` | Code-defined predicate escape hatch | https://projects.reid-corp.com/work_packages/754 |
| `WI-P0-18` |  | `WI-P0-18-decouple-replay-selection-from-interactive-selection.md` | Decouple replay selection from interactive UI selection | https://projects.reid-corp.com/work_packages/755 |
| `WI-P0-19` |  | `WI-P0-19-testharness-inactivity-monitor.md` | TestHarness inactivity monitor | https://projects.reid-corp.com/work_packages/756 |
| `WI-P0-20` |  | `WI-P0-20-harness-catalog-contract.md` | Harness catalog contract records | https://projects.reid-corp.com/work_packages/757 |
| `WI-P0-21` |  | `WI-P0-21-testharness-scenario-descriptors.md` | TestHarness scenario descriptors | https://projects.reid-corp.com/work_packages/758 |
| `WI-P0-22` |  | `WI-P0-22-testharness-parameter-descriptors.md` | TestHarness config parameter descriptors | https://projects.reid-corp.com/work_packages/759 |
| `WI-P0-23` |  | `WI-P0-23-testharness-list-scenarios-and-config-cli.md` | TestHarness `--list-scenarios` and `--config` CLI | https://projects.reid-corp.com/work_packages/760 |
| `WI-P0-24` |  | `WI-P0-24-harness-catalog-client.md` | ReplaySketch harness catalog client | https://projects.reid-corp.com/work_packages/761 |
| `WI-P0-25` |  | `WI-P0-25-harness-run-client.md` | ReplaySketch harness run client | https://projects.reid-corp.com/work_packages/762 |
| `WI-P0-26` |  | `WI-P0-26-harness-launcher-viewmodel-browse-filter.md` | Harness launcher view model: browse and filter | https://projects.reid-corp.com/work_packages/763 |
| `WI-P0-27` |  | `WI-P0-27-harness-parameter-editor-and-run.md` | Harness parameter editor and run command | https://projects.reid-corp.com/work_packages/764 |
| `WI-P0-28` |  | `WI-P0-28-harness-launch-presets.md` | Harness launch presets | https://projects.reid-corp.com/work_packages/765 |
| `WI-P0-29` |  | `WI-P0-29-harness-avalonia-tab.md` | ReplaySketch Harness tab (Avalonia) | https://projects.reid-corp.com/work_packages/766 |
| `WI-P1-01` |  | `WI-P1-01-map-player-relationship-code.md` | Map the player-relationship gap | https://projects.reid-corp.com/work_packages/767 |
| `WI-P1-02` |  | `WI-P1-02-implement-onnewgame-relationships.md` | Implement player-relationship setup in `OnNewGame` | https://projects.reid-corp.com/work_packages/768 |
| `WI-P1-03` |  | `WI-P1-03-test-reciprocal-relationships.md` | Unit test for reciprocal player relationships | https://projects.reid-corp.com/work_packages/769 |
| `WI-P1-04` |  | `WI-P1-04-unified-skirmish-spawn.md` | Unified skirmish spawn path | https://projects.reid-corp.com/work_packages/770 |
| `WI-P1-05` |  | `WI-P1-05-ai-slot-fill.md` | AI slot fill before `StartGame` | https://projects.reid-corp.com/work_packages/771 |
| `WI-P1-06` |  | `WI-P1-06-boot-validation.md` | Boot validation: human + AI skirmish boots headlessly | https://projects.reid-corp.com/work_packages/772 |
| `WI-P1-07` |  | `WI-P1-07-player-ownership-tracking.md` | Per-player object-ownership tracking | https://projects.reid-corp.com/work_packages/773 |
| `WI-P1-08` |  | `WI-P1-08-fix-new-file-encoding.md` | Restore UTF-8 BOM on new P1 files (format gate) | https://projects.reid-corp.com/work_packages/774 |
| `WI-P1-09` |  | `WI-P1-09-fix-startpos-collision.md` | Fix start-position collision in `SkirmishSlotDefaults.ApplyDefaults` | https://projects.reid-corp.com/work_packages/775 |
| `WI-P1-10` |  | `WI-P1-10-verify-team-sentinel.md` | Verify/fix team sentinel for default (FFA) slots | https://projects.reid-corp.com/work_packages/776 |
| `WI-P1-11` |  | `WI-P1-11-owner-transfer-tracking.md` | Maintain owned-object counts on `GameObject.Owner` reassignment | https://projects.reid-corp.com/work_packages/777 |
| `WI-P2-01` |  | `WI-P2-01-ai-tick-scaffold.md` | AI tick scaffold | https://projects.reid-corp.com/work_packages/778 |
| `WI-P2-02` |  | `WI-P2-02-economy-and-base-buildout.md` | Economy and base build-out | https://projects.reid-corp.com/work_packages/779 |
| `WI-P2-03` |  | `WI-P2-03-unit-production.md` | Unit production | https://projects.reid-corp.com/work_packages/780 |
| `WI-P2-04` |  | `WI-P2-04-military-behavior.md` | Military behavior | https://projects.reid-corp.com/work_packages/781 |
| `WI-P2-05` |  | `WI-P2-05-scenario-matrix.md` | P2 scenario matrix | https://projects.reid-corp.com/work_packages/782 |
| `WI-P3-01` |  | `WI-P3-01-gameoutcome-victorysystem-scaffold.md` | GameOutcome and VictorySystem scaffold | https://projects.reid-corp.com/work_packages/783 |
| `WI-P3-02` |  | `WI-P3-02-defeat-and-win-rules.md` | Defeat and win rules | https://projects.reid-corp.com/work_packages/784 |
| `WI-P3-03` |  | `WI-P3-03-death-to-defeat-event.md` | Death-to-defeat lifecycle event | https://projects.reid-corp.com/work_packages/785 |
| `WI-P3-04` |  | `WI-P3-04-end-of-match-flow.md` | End-of-match flow and EndGame handling | https://projects.reid-corp.com/work_packages/786 |
| `WI-P3-05` |  | `WI-P3-05-mutual-elimination-sudden-death.md` | Mutual elimination to sudden death | https://projects.reid-corp.com/work_packages/787 |
| `WI-P3-06` |  | `WI-P3-06-time-limit-score-decision.md` | Time-limit score decision | https://projects.reid-corp.com/work_packages/788 |
| `WI-P3-07` |  | `WI-P3-07-scenario-matrix.md` | P3 scenario matrix | https://projects.reid-corp.com/work_packages/789 |
| `WI-P4-01` |  | `WI-P4-01-gather-deposit-verification.md` | Gather-to-deposit verification | https://projects.reid-corp.com/work_packages/790 |
| `WI-P4-02` |  | `WI-P4-02-spend-debit-verification.md` | Spend/debit and refund verification | https://projects.reid-corp.com/work_packages/791 |
| `WI-P4-03` |  | `WI-P4-03-power-loop.md` | Power loop verification | https://projects.reid-corp.com/work_packages/792 |
| `WI-P4-04` |  | `WI-P4-04-supply-cycle-gap-closure.md` | Supply, worker, and dozer cycle gap closure | https://projects.reid-corp.com/work_packages/793 |
| `WI-P4-05` |  | `WI-P4-05-scenario-matrix.md` | P4 economy scenario matrix | https://projects.reid-corp.com/work_packages/794 |
| `WI-P5-01` |  | `WI-P5-01-building-placement.md` | Player + AI building placement | https://projects.reid-corp.com/work_packages/795 |
| `WI-P5-02` |  | `WI-P5-02-prerequisite-tech-gating.md` | Prerequisite and tech gating | https://projects.reid-corp.com/work_packages/796 |
| `WI-P5-03` |  | `WI-P5-03-unit-upgrade-production.md` | Unit and upgrade production | https://projects.reid-corp.com/work_packages/797 |
| `WI-P5-04` |  | `WI-P5-04-control-bar-buildability-binding.md` | Control-bar buildability binding | https://projects.reid-corp.com/work_packages/798 |
| `WI-P5-05` |  | `WI-P5-05-scenario-matrix.md` | P5 scenario matrix | https://projects.reid-corp.com/work_packages/799 |
| `WI-P6-01` |  | `WI-P6-01-command-coverage-matrix.md` | Command coverage matrix | https://projects.reid-corp.com/work_packages/800 |
| `WI-P6-02` |  | `WI-P6-02-combat-coverage.md` | Combat coverage | https://projects.reid-corp.com/work_packages/801 |
| `WI-P6-03` |  | `WI-P6-03-death-cleanup.md` | Death cleanup | https://projects.reid-corp.com/work_packages/802 |
| `WI-P6-04` |  | `WI-P6-04-scenario-matrix.md` | P6 scenario matrix | https://projects.reid-corp.com/work_packages/803 |
| `WI-P6-05` |  | `WI-P6-05-attack-object-approach.md` | Attack-object approach gap | https://projects.reid-corp.com/work_packages/804 |
| `WI-P6-06` |  | `WI-P6-06-design-jet-crash-lifecycle.md` | Ground the jet crash lifecycle | https://projects.reid-corp.com/work_packages/805 |
| `WI-P6-07` |  | `WI-P6-07-implement-jet-crash-lifecycle.md` | Implement jet crash and final cleanup | https://projects.reid-corp.com/work_packages/806 |
| `WI-P6b-01` |  | `WI-P6b-01-infantry-contract.md` | Infantry archetype contract | https://projects.reid-corp.com/work_packages/807 |
| `WI-P6b-02` |  | `WI-P6b-02-vehicle-contract.md` | Vehicle archetype contract | https://projects.reid-corp.com/work_packages/808 |
| `WI-P6b-03` |  | `WI-P6b-03-aircraft-contract.md` | Aircraft archetype contract | https://projects.reid-corp.com/work_packages/809 |
| `WI-P6b-04` |  | `WI-P6b-04-builder-contract.md` | Builder archetype contract | https://projects.reid-corp.com/work_packages/810 |
| `WI-P6b-05` |  | `WI-P6b-05-support-contract.md` | Support archetype contract | https://projects.reid-corp.com/work_packages/811 |
| `WI-P6b-06` |  | `WI-P6b-06-superweapon-contract.md` | Superweapon archetype contract | https://projects.reid-corp.com/work_packages/812 |
| `WI-P6b-07` |  | `WI-P6b-07-special-power-gap-closure.md` | Special-power gap closure | https://projects.reid-corp.com/work_packages/813 |
| `WI-P6b-08` |  | `WI-P6b-08-scenario-matrix.md` | P6b scenario matrix | https://projects.reid-corp.com/work_packages/814 |
| `WI-P6c-01` |  | `WI-P6c-01-special-power-implementation.md` | Special-power implementation | https://projects.reid-corp.com/work_packages/815 |
| `WI-P7-01` |  | `WI-P7-01-player-eliminated-scenario.md` | PlayerEliminated regression scenario | https://projects.reid-corp.com/work_packages/816 |
| `WI-P7-02` |  | `WI-P7-02-player-surrenders-scenario.md` | PlayerSurrenders regression scenario | https://projects.reid-corp.com/work_packages/817 |
| `WI-P7-03` |  | `WI-P7-03-ai-surrenders-scenario.md` | AISurrenders regression scenario | https://projects.reid-corp.com/work_packages/818 |
| `WI-P7-04` |  | `WI-P7-04-team-win-scenario.md` | TeamWin regression scenario | https://projects.reid-corp.com/work_packages/819 |
| `WI-P7-05` |  | `WI-P7-05-mutual-elimination-scenario.md` | MutualElimination regression scenario | https://projects.reid-corp.com/work_packages/820 |
| `WI-P7-06` |  | `WI-P7-06-score-decision-scenario.md` | ScoreDecision regression scenario | https://projects.reid-corp.com/work_packages/821 |
| `WI-P7-07` |  | `WI-P7-07-universal-invariants-and-matrix.md` | Universal invariants and P7 scenario matrix | https://projects.reid-corp.com/work_packages/822 |
| `WI-P7-08` |  | `WI-P7-08-regression-lock.md` | P7 regression lock | https://projects.reid-corp.com/work_packages/823 |
| `WI-P8-01` |  | `WI-P8-01-data-driven-victory-config.md` | Data-driven victory configuration seam | https://projects.reid-corp.com/work_packages/824 |
| `WI-P8-02` |  | `WI-P8-02-sudden-death-repeat-fix.md` | Sudden-death repeat-elimination fix | https://projects.reid-corp.com/work_packages/825 |
| `WI-P8-03` |  | `WI-P8-03-end-of-match-ui-and-testharness.md` | End-of-match UI and TestHarness integration | https://projects.reid-corp.com/work_packages/826 |
| `WI-P8-04` |  | `WI-P8-04-real-gather-cycle-harness.md` | Real gather-cycle harness verification | https://projects.reid-corp.com/work_packages/827 |
| `WI-P8-05` |  | `WI-P8-05-real-faction-build-trees.md` | Real per-faction build trees and production assertions | https://projects.reid-corp.com/work_packages/828 |
| `WI-P8-06` |  | `WI-P8-06-control-bar-live-dispatch.md` | Control-bar live dispatch verification | https://projects.reid-corp.com/work_packages/829 |
| `WI-P8-07` |  | `WI-P8-07-fireweaponupdate-and-real-effects.md` | FireWeaponUpdate implementation and real observable-effect assertions | https://projects.reid-corp.com/work_packages/830 |
| `WI-P9-01` |  | `WI-P9-01-primary-action-model.md` | Primary-action model | https://projects.reid-corp.com/work_packages/831 |
| `WI-P9-02` |  | `WI-P9-02-scenario-layout-builder.md` | Scenario and fixed layout builder | https://projects.reid-corp.com/work_packages/832 |
| `WI-P9-03` |  | `WI-P9-03-direct-spawn-host.md` | Direct-spawn host | https://projects.reid-corp.com/work_packages/833 |
| `WI-P9-04` |  | `WI-P9-04-unit-action-timer.md` | Per-unit action timer | https://projects.reid-corp.com/work_packages/834 |
| `WI-P9-05` |  | `WI-P9-05-testharness-wiring.md` | TestHarness wiring for both run modes | https://projects.reid-corp.com/work_packages/835 |
| `WI-P9-06` |  | `WI-P9-06-timing-report-output.md` | Timing report output | https://projects.reid-corp.com/work_packages/836 |
| `WI-P9-07` |  | `WI-P9-07-golden-baseline-regression-gate.md` | Golden baseline and regression gate | https://projects.reid-corp.com/work_packages/837 |
| `WI-P9-08` |  | `WI-P9-08-validation-ground-tests.md` | Validation-ground test coverage | https://projects.reid-corp.com/work_packages/838 |
| `WI-R2-01` |  | `WI-R2-01-sedan-operator-proof.md` | Sedan real-world preview proof | https://projects.reid-corp.com/work_packages/839 |
| `WI-R2-02` |  | `WI-R2-02-provider-source-profiles.md` | Provider source profiles for data preparation | https://projects.reid-corp.com/work_packages/840 |
| `WI-R2-03` |  | `WI-R2-03-building-footprint-masks.md` | Building footprints as terrain masks | https://projects.reid-corp.com/work_packages/841 |
| `WI-R3-01` |  | `WI-R3-01-procedural-building-investigation.md` | Procedural buildings from footprints (investigation) | https://projects.reid-corp.com/work_packages/842 |
| `WI-REN-01` |  | `WI-REN-01-rename-sagerandom.md` | Rename `SageRandom` to `PanopticonRandom` | https://projects.reid-corp.com/work_packages/843 |
| `WI-VG-05` |  | `WI-VG-05-dark-mountain-validation-ground.md` | Switch the validation ground to Dark Mountain | https://projects.reid-corp.com/work_packages/844 |
| `WI-W0-01` |  | `WI-W0-01-scaffold-western-front-project.md` | Scaffold the Western Front project and stable keys | https://projects.reid-corp.com/work_packages/845 |
| `WI-W0-02` |  | `WI-W0-02-western-front-state-schema.md` | Add the Western Front state schema | https://projects.reid-corp.com/work_packages/846 |
| `WI-W0-03` |  | `WI-W0-03-synthetic-topology.md` | Build the synthetic topology fixture | https://projects.reid-corp.com/work_packages/847 |
| `WI-W0-04` |  | `WI-W0-04-theater-clock-scheduler.md` | Add the theater clock and scheduler | https://projects.reid-corp.com/work_packages/848 |
| `WI-W0-05` |  | `WI-W0-05-formation-registry-modules.md` | Add state modules and the formation registry | https://projects.reid-corp.com/work_packages/849 |
| `WI-W0-06` |  | `WI-W0-06-synthetic-scenario.md` | Build the synthetic W0 scenario | https://projects.reid-corp.com/work_packages/850 |
| `WI-W0-07` |  | `WI-W0-07-western-front-domain-adapter.md` | Add the Western Front domain adapter | https://projects.reid-corp.com/work_packages/851 |
| `WI-W0-08` |  | `WI-W0-08-conservation-ledger.md` | Add the conservation ledger | https://projects.reid-corp.com/work_packages/852 |
| `WI-W0-09` |  | `WI-W0-09-charter-fixture-evidence.md` | Prove the W0 charter fixture evidence | https://projects.reid-corp.com/work_packages/853 |
| `WI-W1-01` |  | `WI-W1-01-parameters-loader.md` | Add Western Front parameters with unit schema | https://projects.reid-corp.com/work_packages/854 |
| `WI-W1-02` |  | `WI-W1-02-schema-v2.md` | Extend domain state schema to version 2 | https://projects.reid-corp.com/work_packages/855 |
| `WI-W1-03` |  | `WI-W1-03-seeded-streams-poisson.md` | Add deterministic streams and Poisson sampler | https://projects.reid-corp.com/work_packages/856 |
| `WI-W1-04` |  | `WI-W1-04-message-queue-commlinks.md` | Add message queue, comm links, and communication system | https://projects.reid-corp.com/work_packages/857 |
| `WI-W1-05` |  | `WI-W1-05-intelligence-echelon-view.md` | Add intelligence pictures and echelon views | https://projects.reid-corp.com/work_packages/858 |
| `WI-W1-06` |  | `WI-W1-06-command-policies.md` | Add command policy interface and reactive rules | https://projects.reid-corp.com/work_packages/859 |
| `WI-W1-07` |  | `WI-W1-07-movement-system.md` | Add operational movement and stacking | https://projects.reid-corp.com/work_packages/860 |
| `WI-W1-08` |  | `WI-W1-08-logistics-allocation.md` | Add logistics allocation with largest remainder | https://projects.reid-corp.com/work_packages/861 |
| `WI-W1-09` |  | `WI-W1-09-shipments-interdiction-consumption.md` | Add shipments, interdiction, and consumption | https://projects.reid-corp.com/work_packages/862 |
| `WI-W1-10` |  | `WI-W1-10-aggregate-combat-stochastic.md` | Add stochastic aggregate combat | https://projects.reid-corp.com/work_packages/863 |
| `WI-W1-11` |  | `WI-W1-11-aggregate-combat-mean-field.md` | Add mean-field aggregate combat variant | https://projects.reid-corp.com/work_packages/864 |
| `WI-W1-12` |  | `WI-W1-12-morale-terrain-outcomes.md` | Add morale, cohesion, outcome, and terrain effects | https://projects.reid-corp.com/work_packages/865 |
| `WI-W1-13` |  | `WI-W1-13-campaign-systems.md` | Add weather, production, and entrenchment systems | https://projects.reid-corp.com/work_packages/866 |
| `WI-W1-14` |  | `WI-W1-14-scenario-evidence-tests.md` | Add W1 scenario and evidence tests | https://projects.reid-corp.com/work_packages/867 |
| `WI-W1-15` |  | `WI-W1-15-a2-experiment-report.md` | Add the 64-seed A2 experiment report | https://projects.reid-corp.com/work_packages/868 |
| `WI-W2-01` |  | `WI-W2-01-activation-policy-hysteresis.md` | Add activation policies and hysteresis | https://projects.reid-corp.com/work_packages/869 |
| `WI-W2-02` |  | `WI-W2-02-transition-records-schema.md` | Add transition records and schema additions | https://projects.reid-corp.com/work_packages/870 |
| `WI-W2-03` |  | `WI-W2-03-tactical-template-map.md` | Map sides and roles to placeholder object definitions | https://projects.reid-corp.com/work_packages/871 |
| `WI-W2-04` |  | `WI-W2-04-soldier-placement.md` | Place soldiers from posture and terrain | https://projects.reid-corp.com/work_packages/872 |
| `WI-W2-05` |  | `WI-W2-05-promotion-ledger-split.md` | Split platoon holdings onto soldiers | https://projects.reid-corp.com/work_packages/873 |
| `WI-W2-06` |  | `WI-W2-06-soldier-casualty-classification.md` | Classify soldier deaths into casualty sinks | https://projects.reid-corp.com/work_packages/874 |
| `WI-W2-07` |  | `WI-W2-07-soldier-ammo-module.md` | Add per-soldier SAA consumption | https://projects.reid-corp.com/work_packages/875 |
| `WI-W2-08` |  | `WI-W2-08-mg-teams.md` | Preserve MG teams through promotion and demotion | https://projects.reid-corp.com/work_packages/876 |
| `WI-W2-09` |  | `WI-W2-09-artillery-impacts-active-areas.md` | Apply strategic artillery impacts in active areas | https://projects.reid-corp.com/work_packages/877 |
| `WI-W2-10` |  | `WI-W2-10-formation-objectives-to-soldier-orders.md` | Translate formation objectives to A4-3 orders | https://projects.reid-corp.com/work_packages/878 |
| `WI-W2-11` |  | `WI-W2-11-demotion-reconciliation-terrain.md` | Demote soldiers and fold terrain summaries back | https://projects.reid-corp.com/work_packages/879 |
| `WI-W2-12` |  | `WI-W2-12-mode-switching-scheduler.md` | Switch modes with the W0 scheduler | https://projects.reid-corp.com/work_packages/880 |
| `WI-W2-13` |  | `WI-W2-13-all-tactical-reference-run.md` | Create the whole-sector all-tactical reference run | https://projects.reid-corp.com/work_packages/881 |
| `WI-W2-14` |  | `WI-W2-14-error-report-64-seeds.md` | Report aggregate error against all-tactical reference | https://projects.reid-corp.com/work_packages/882 |
| `WI-W3a-01` |  | `WI-W3a-01-scale-probe.md` | Measure Sedan tactical scale | https://projects.reid-corp.com/work_packages/883 |
| `WI-W3a-02` |  | `WI-W3a-02-operator-geospatial-inputs.md` | Document Sedan geospatial inputs | https://projects.reid-corp.com/work_packages/884 |
| `WI-W3a-03` |  | `WI-W3a-03-sedan-payload-validation.md` | Validate the Sedan payload | https://projects.reid-corp.com/work_packages/885 |
| `WI-W3a-04` |  | `WI-W3a-04-topology-builder-contract.md` | Define the topology builder contract | https://projects.reid-corp.com/work_packages/886 |
| `WI-W3a-05` |  | `WI-W3a-05-region-cell-grid.md` | Derive 1000 m regions and 125 m cells | https://projects.reid-corp.com/work_packages/887 |
| `WI-W3a-06` |  | `WI-W3a-06-vector-feature-to-topology.md` | Map generated features to topology | https://projects.reid-corp.com/work_packages/888 |
| `WI-W3a-07` |  | `WI-W3a-07-neutral-territory.md` | Enforce neutral territory | https://projects.reid-corp.com/work_packages/889 |
| `WI-W3a-08` |  | `WI-W3a-08-order-of-battle-schema.md` | Define the order-of-battle schema | https://projects.reid-corp.com/work_packages/890 |
| `WI-W3a-09` |  | `WI-W3a-09-french-order-of-battle.md` | Transcribe the French order of battle | https://projects.reid-corp.com/work_packages/891 |
| `WI-W3a-10` |  | `WI-W3a-10-german-order-of-battle.md` | Transcribe the German order of battle | https://projects.reid-corp.com/work_packages/892 |
| `WI-W3a-11` |  | `WI-W3a-11-organization-template-expansion.md` | Expand period organization templates | https://projects.reid-corp.com/work_packages/893 |
| `WI-W3a-12` |  | `WI-W3a-12-period-ledger-families.md` | Add period ledger families | https://projects.reid-corp.com/work_packages/894 |
| `WI-W3a-13` |  | `WI-W3a-13-period-weapon-parameters.md` | Verify period weapon parameters | https://projects.reid-corp.com/work_packages/895 |
| `WI-W3a-14` |  | `WI-W3a-14-period-content-definitions.md` | Add period content definitions | https://projects.reid-corp.com/work_packages/896 |
| `WI-W3a-15` |  | `WI-W3a-15-weapon-hit-resolution.md` | Add weapon hit resolution | https://projects.reid-corp.com/work_packages/897 |
| `WI-W3a-16` |  | `WI-W3a-16-shells-and-fuzes.md` | Model shells and fuzes | https://projects.reid-corp.com/work_packages/898 |
| `WI-W3a-17` |  | `WI-W3a-17-line-of-sight.md` | Require line of sight | https://projects.reid-corp.com/work_packages/899 |
| `WI-W3a-18` |  | `WI-W3a-18-environment-schedule.md` | Apply the environment schedule | https://projects.reid-corp.com/work_packages/900 |
| `WI-W3a-19` |  | `WI-W3a-19-tactical-artillery.md` | Implement tactical artillery | https://projects.reid-corp.com/work_packages/901 |
| `WI-W3a-20` |  | `WI-W3a-20-cavalry-troopers.md` | Implement cavalry troopers | https://projects.reid-corp.com/work_packages/902 |
| `WI-W3a-21` |  | `WI-W3a-21-courier-and-telegraph-links.md` | Add courier and telegraph links | https://projects.reid-corp.com/work_packages/903 |
| `WI-W3a-22` |  | `WI-W3a-22-orders-timeline-dataset.md` | Transcribe the orders timeline | https://projects.reid-corp.com/work_packages/904 |
| `WI-W3a-23` |  | `WI-W3a-23-order-applicability.md` | Apply timeline orders | https://projects.reid-corp.com/work_packages/905 |
| `WI-W3a-24` |  | `WI-W3a-24-local-surrender.md` | Add local surrender | https://projects.reid-corp.com/work_packages/906 |
| `WI-W3a-25` |  | `WI-W3a-25-army-capitulation.md` | Add army capitulation | https://projects.reid-corp.com/work_packages/907 |
| `WI-W3a-26` |  | `WI-W3a-26-sedan-scenario-assembly.md` | Assemble the Sedan scenario | https://projects.reid-corp.com/work_packages/908 |
| `WI-W3a-27` |  | `WI-W3a-27-a5-geospatial-rendering.md` | Render geospatial topology in A5 | https://projects.reid-corp.com/work_packages/909 |
| `WI-W3a-28` |  | `WI-W3a-28-all-tactical-reference-run.md` | Run all-tactical reference seeds | https://projects.reid-corp.com/work_packages/910 |
| `WI-W3a-29` |  | `WI-W3a-29-aggregate-and-mixed-runs.md` | Run aggregate and mixed arms | https://projects.reid-corp.com/work_packages/911 |
| `WI-W3a-30` |  | `WI-W3a-30-sedan-evidence-report.md` | Publish Sedan evidence | https://projects.reid-corp.com/work_packages/912 |
| `WI-W3a-31` |  | `WI-W3a-31-operational-recording-adapter.md` | Add Western Front operational recording adapter | https://projects.reid-corp.com/work_packages/913 |
| `WI-W3b-01` |  | `WI-W3b-01-operator-geospatial-inputs.md` | Document operator-local Somme geospatial inputs | https://projects.reid-corp.com/work_packages/914 |
| `WI-W3b-02` |  | `WI-W3b-02-somme-payload-validation.md` | Validate W3b Somme payload metadata | https://projects.reid-corp.com/work_packages/915 |
| `WI-W3b-03` |  | `WI-W3b-03-synthetic-forces-on-geography.md` | Place synthetic W forces on generated geography | https://projects.reid-corp.com/work_packages/916 |
| `WI-W3b-04` |  | `WI-W3b-04-run-w1-evidence-on-sector.md` | Run W1 evidence on generated sector | https://projects.reid-corp.com/work_packages/917 |
| `WI-W3b-05` |  | `WI-W3b-05-run-w2-evidence-on-sector.md` | Run W2 evidence on generated sector | https://projects.reid-corp.com/work_packages/918 |
| `WI-W3b-06` |  | `WI-W3b-06-geospatial-sector-report.md` | Write the W3b evidence report | https://projects.reid-corp.com/work_packages/919 |
| `WI-W4-01` |  | `WI-W4-01-matrix-axis-contract.md` | Define the W4 matrix axis contract | https://projects.reid-corp.com/work_packages/920 |
| `WI-W4-02` |  | `WI-W4-02-parameter-binding.md` | Bind W4 axes to Western Front parameters | https://projects.reid-corp.com/work_packages/921 |
| `WI-W4-03` |  | `WI-W4-03-information-audit.md` | Implement the information audit | https://projects.reid-corp.com/work_packages/922 |
| `WI-W4-04` |  | `WI-W4-04-cell-result-schema.md` | Create the W4 cell result schema | https://projects.reid-corp.com/work_packages/923 |
| `WI-W4-05` |  | `WI-W4-05-sensitivity-report.md` | Generate W4 sensitivity report | https://projects.reid-corp.com/work_packages/924 |
| `WI-W4-06` |  | `WI-W4-06-a5-comparison-workspace.md` | Open W4 run sets in A5 | https://projects.reid-corp.com/work_packages/925 |
| `WI-W5-01` |  | `WI-W5-01-front-strip-partition.md` | Implement the front-strip partition | https://projects.reid-corp.com/work_packages/926 |
| `WI-W5-02` |  | `WI-W5-02-shard-slice-state.md` | Create Western Front shard slice state | https://projects.reid-corp.com/work_packages/927 |
| `WI-W5-03` |  | `WI-W5-03-inprocess-json-transport.md` | Add in-process JSON boundary transport | https://projects.reid-corp.com/work_packages/928 |
| `WI-W5-04` |  | `WI-W5-04-lockstep-coordinator.md` | Build the W5 lockstep coordinator | https://projects.reid-corp.com/work_packages/929 |
| `WI-W5-05` |  | `WI-W5-05-cross-boundary-handoff.md` | Implement cross-boundary handoff | https://projects.reid-corp.com/work_packages/930 |
| `WI-W5-06` |  | `WI-W5-06-boundary-effects.md` | Apply logistics and command boundary effects | https://projects.reid-corp.com/work_packages/931 |
| `WI-W5-07` |  | `WI-W5-07-sharded-vs-reference-evidence.md` | Compare sharded runs with the reference | https://projects.reid-corp.com/work_packages/932 |
| `WI-W5-08` |  | `WI-W5-08-a5-shard-visualization.md` | Visualize W5 shards in A5 | https://projects.reid-corp.com/work_packages/933 |
| `WI-W6-01` |  | `WI-W6-01-campaign-file.md` | Define the W6 campaign file | https://projects.reid-corp.com/work_packages/934 |
| `WI-W6-02` |  | `WI-W6-02-historical-oob-investigation.md` | Investigate historical OOB source format | https://projects.reid-corp.com/work_packages/935 |
| `WI-W6-03` |  | `WI-W6-03-long-run-clock-plan.md` | Implement the long-run clock plan | https://projects.reid-corp.com/work_packages/936 |
| `WI-W6-04` |  | `WI-W6-04-domain-snapshot-save-load.md` | Save and load campaign checkpoints | https://projects.reid-corp.com/work_packages/937 |
| `WI-W6-05` |  | `WI-W6-05-replay-indexing.md` | Index long campaign artifacts for replay | https://projects.reid-corp.com/work_packages/938 |
| `WI-W6-06` |  | `WI-W6-06-long-run-metrics.md` | Collect W6 long-run metrics | https://projects.reid-corp.com/work_packages/939 |
| `WI-W6-07` |  | `WI-W6-07-approximation-error-report.md` | Report active fidelity approximation error | https://projects.reid-corp.com/work_packages/940 |
| `WI-W6-08` |  | `WI-W6-08-campaign-evidence-report.md` | Write the W6 campaign evidence report | https://projects.reid-corp.com/work_packages/941 |
| `WI-X1-01` |  | `WI-X1-01-hit-feedback-policy.md` | Hit-feedback policy audit | https://projects.reid-corp.com/work_packages/942 |
| `WI-X1-02` |  | `WI-X1-02-imported-damage-fx-binding.md` | Imported-vehicle damage effects | https://projects.reid-corp.com/work_packages/943 |
| `WI-X1-03` |  | `WI-X1-03-hit-feedback-regressions.md` | Hit-feedback regression coverage | https://projects.reid-corp.com/work_packages/944 |
| `WI-X1-04` |  | `WI-X1-04-readable-duel-capture.md` | Readable mixed-source duel capture | https://projects.reid-corp.com/work_packages/945 |
| `WI-X1-05` |  | `WI-X1-05-target-impact-design.md` | Directional target impacts and Rebel feedback design | https://projects.reid-corp.com/work_packages/946 |
| `WI-X1-06` |  | `WI-X1-06-imported-impact-effect-selection.md` | Imported impact-effect selection | https://projects.reid-corp.com/work_packages/947 |
| `WI-X1-07` |  | `WI-X1-07-attacker-facing-impact-placement.md` | Attacker-facing impact placement | https://projects.reid-corp.com/work_packages/948 |
| `WI-X2-01` |  | `WI-X2-01-bound-upload-resource-lifetimes.md` | Bound temporary graphics-upload resource lifetimes | https://projects.reid-corp.com/work_packages/949 |
| `WI-X2-02` |  | `WI-X2-02-reuse-combat-fx-model-resources.md` | Reduce combat FX/model resource recreation | https://projects.reid-corp.com/work_packages/950 |
| `WI-X3-01` |  | `WI-X3-01-cursor-theme-examples.md` | Open-source cursor theme evaluation and examples | https://projects.reid-corp.com/work_packages/951 |
| `WI-X4-01` |  | `WI-X4-01-replaysketch-tabcontrol.md` | Switch the ReplaySketch shell to `TabControl` | https://projects.reid-corp.com/work_packages/952 |
| `WI-X5-01` |  | `WI-X5-01-culling-dispatch-sizing.md` | Right-size parallel culling dispatch | https://projects.reid-corp.com/work_packages/953 |
| `WI-X6-01` |  | `WI-X6-01-extract-ai-navigation.md` | Extract navigation logic from AIUpdate | https://projects.reid-corp.com/work_packages/954 |
| `WI-X7-01` |  | `WI-X7-01-types-subfolders.md` | Organize non-logic types into `Types` subfolders | https://projects.reid-corp.com/work_packages/955 |
