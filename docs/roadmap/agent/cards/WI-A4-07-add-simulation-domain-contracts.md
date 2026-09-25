# Task Card `WI-A4-07`: Add simulation-domain contracts to the host

| Field | Value |
|---|---|
| Card ID | `WI-A4-07` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-1 Simulation Domain Model seam |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A4-06` |
| Diagrams | A4 domain diagrams created by `WI-A4-06` |

## Goal

Introduce the designed domain interfaces and inject one domain model into `SimulationHost` without
changing existing RTS behavior.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-simulation-domain-model.md`.
- `src/Panopticon.Simulation/SimulationHost.cs`.
- `src/Panopticon.Simulation/IGame.cs`, `IGameEngine.cs`, and `GameEngine.cs`.
- `src/Panopticon.Simulation/Recording/RunRecording.cs`.
- Existing `SimulationHostTests`.

## Steps

1. Add the domain interfaces and immutable context/value types named by the design.
2. Add one required or explicitly defaulted domain-model construction path.
3. Route host lifecycle and recording callbacks through the domain contract.
4. Preserve existing constructor behavior through the documented RTS default.
5. Add focused construction and lifecycle-order tests.

## Files likely to change

- New files under `src/Panopticon.Simulation/Simulation/`
- `src/Panopticon.Simulation/SimulationHost.cs`
- `test/Panopticon.Simulation.Tests/SimulationHostTests.cs`

## Acceptance criteria

- [ ] The host depends on the domain interface, not an RTS concrete type.
- [ ] Domain lifecycle callbacks execute in the documented order.
- [ ] Existing host tests retain their outcomes and run-output shape.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SimulationHostTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not move `GameObject` state or implement graph behavior in this card.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-08`.
