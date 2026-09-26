# Task Card `WI-W0-09`: Prove the W0 charter fixture evidence

| Field | Value |
|---|---|
| Card ID | `WI-W0-09` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W0-08` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Add the end-to-end evidence test for the W0 milestone and close the phase.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §10.
- `src/Panopticon.Simulation/Recording/RunRecording.cs`: `Attach`, `Complete`, `DomainState`.
- `test/Panopticon.Simulation.Tests/Simulation/SimulationDomainContractTests.cs`: host fixture.

## Steps

1. Add `WesternFrontCharterFixtureTests` on `MockedGameTest` running the §10 sequence with a repository-local artifact path and cadence options of 10 s operational, 60 s campaign.
2. Register a test system that performs one scripted ledger operation of each type on chosen operational ticks.
3. Assert every §10 bullet, loading the artifact through `DomainStateArtifact.Load` for reconstruction comparisons.
4. Close the phase in roadmap docs only if the implementation is actually complete.

## Files likely to change

- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontCharterFixtureTests.cs`
- Roadmap status docs if the implementation completes the phase

## Acceptance criteria

- [ ] Every §10 assertion passes.
- [ ] The test deletes its repository-local artifact.
- [ ] Two mode changes are recorded and both occur on operational boundaries.
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
- List follow-up cards to create: none.
