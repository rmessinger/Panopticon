# Task Card `WI-A1-24`: Prove reconstruction conformance

| Field | Value |
|---|---|
| Card ID | `WI-A1-24` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6g reconstruction conformance |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-A1-22`, `WI-A1-23`, `WI-A4-45` |
| Diagrams | [A1-6 §9](../../deep-dives/a1-6-operational-recording.md#9-sub-items-and-cards) |

## Goal

Add conformance evidence that operational reconstruction matches full recording on RTS and transport-graph domains and that `Full` mode remains unchanged.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §3, §5, §6, and §9: determinism, bundle, reconstruction, and evidence target.
- `test/Panopticon.Simulation.Tests/Recording/TieredRecordingTests.cs`: A1-5 recording tests.
- `test/Panopticon.Simulation.Tests/Reproducibility/SeededRunTests.cs`: deterministic guardrail.
- `src/Panopticon.Simulation/Simulation/Transport/TransportGraphDomainModel.cs`: A4-45 reference domain.
- `src/Panopticon.Simulation/Recording/RunReconstructor.cs`: reconstruction API.

## Steps

1. Add RTS-domain and transport-graph-domain conformance tests that run the same seed in `Full` and `Operational` modes.
2. Choose random frames from the deterministic test seed and reconstruct them through `RunReconstructor`.
3. Assert every recorded operational hash matches during reconstruction.
4. Assert reconstructed frames equal the corresponding `Full` run capture for world state and domain state where present.
5. Assert `Full` mode outputs remain byte-identical to pre-A1-6 expected artifacts.
6. On any determinism or golden-baseline deviation, stop, preserve artifacts, and report before modifying baselines.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Recording/ReconstructionConformanceTests.cs`
- `test/Panopticon.Simulation.Tests/Recording/TieredRecordingTests.cs` if fixture reuse is needed

## Acceptance criteria

- [ ] RTS-domain random frames reconstruct with all operational hashes matching and state equal to the `Full` run of the same seed.
- [ ] Transport-graph-domain random frames reconstruct with all operational hashes matching and domain state equal to the `Full` run of the same seed.
- [ ] `Full` mode outputs are byte-identical to pre-A1-6 output for the selected fixture.
- [ ] Cache use does not change reconstructed output.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ReconstructionConformance|SeededRunTests"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not regenerate golden baselines in this card. Follow the stop-and-report determinism rule for same-seed equality, golden frames, and byte-identity failures.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Close A1-6 only if every prerequisite card is done and conformance passes.
- List follow-up cards to create here:  - none

## Result

Conformance proven on both domains (3/3 `ReconstructionConformanceTests`, installed game):

- RTS: same-seed Full vs Operational (cadence 5/15) runs of the alpine fixture; a seed-derived
  window `[5, 35]` reconstructs with every recorded boundary hash verified, and each delivered
  summary equals the Full run's per-frame summary byte-for-byte.
- Transport-graph: full 600-frame run to `AllRoutesComplete`; reconstruction delivers non-null
  domain state at every boundary, each frame equals the Full run's live-captured projection
  byte-for-byte, vehicle conservation residuals stay empty across reconstruction, and the
  operational run reaches the same terminal edge throughput as the Full run.
- Cache: a second reconstruction served from cached boundary checkpoints delivers identical
  frames and summaries.
- `Full` byte-identity: `Attach_WithFullOptions_ProducesByteIdenticalJsonToOmittedOptions`
  (WI-A1-19) plus manifest-field absence assertions here; there is no committed pre-A1-6 byte
  fixture by design.

Enablers required by conformance (the WI-A1-16 finding):

- `TransportGraphDomainModel` now registers its controller and shared vehicle template
  definitions in the game's asset store so checkpoint restore can recreate objects by name,
  and implements `IReconstructableDomain` (cadence, rollups, events, `PersistRuntime`
  rebuilding occupancy/throughput/topology keys/authority resolutions/controllers/vehicles).
- Per-vehicle routes moved from module data to `TransportVehicleUpdate` object state with a
  `Load` override, so one shared template serves every vehicle and routes round-trip.
- `WeaponSet.Persist` read now `TryGetValue`s the definition's weapon-set map: weaponless
  code-built objects persist empty conditions and previously threw `KeyNotFoundException`
  on restore.

Validation: focused suites 82/82 with install (Recording, Transport, contract, checkpoint,
SeededRun guardrail), `ReconstructionConformanceTests` 3/3, `dotnet format` verify 0 errors,
brace check and `git diff --check` clean.
