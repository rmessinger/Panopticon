# Task Card `WI-W0-09`: Prove the W0 charter fixture evidence

| Field | Value |
|---|---|
| Card ID | `WI-W0-09` |
| Roadmap phase | [Track W · W0](../../track-w-western-front.md): charter fixture |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W0-08` |
| Diagrams | [W0 §12](../../deep-dives/w0-charter-fixture.md#12-architecture-diagrams) |

## Goal

Add the end-to-end evidence test for the W0 milestone and close the phase.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §10.
- `src/Panopticon.Simulation/Recording/RunRecording.cs`: `Attach`, `Complete`, `DomainState`.
- `test/Panopticon.Simulation.Tests/Simulation/SimulationDomainContractTests.cs`: host fixture.

## Steps

1. Add `WesternFrontCharterFixtureTests` on `MockedGameTest` running the §10 sequence with a
   temporary domain-state artifact path and cadence options of 10 s operational, 60 s campaign.
2. Register a test system that performs one scripted ledger operation of each type on chosen
   operational ticks.
3. Assert every §10 bullet, loading the artifact through `DomainStateArtifact.Load` for the
   reconstruction comparisons.
4. Close the phase: W0 `Done` in the track doc, checklist ticks, README progress, and the charter's
   WF-0 status line.

## Files likely to change

- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontCharterFixtureTests.cs`
- `docs/roadmap/track-w-western-front.md`, `docs/roadmap/implementation-checklist.md`,
  `docs/roadmap/README.md`, `docs/roadmap/deep-dives/western-front-scale-simulation.md`

## Acceptance criteria

- [ ] Every §10 assertion passes.
- [ ] The test deletes its temporary artifact.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontCharterFixture"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No W1 systems. If an assertion needs a schema change, stop and update the deep dive first.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Follow-up: decompose W1 into `WI-W1-*` cards from [w1-aggregate-sector.md](../../deep-dives/w1-aggregate-sector.md).
