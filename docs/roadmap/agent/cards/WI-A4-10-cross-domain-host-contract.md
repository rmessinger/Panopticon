# Task Card `WI-A4-10`: Prove the cross-domain host contract

| Field | Value |
|---|---|
| Card ID | `WI-A4-10` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-1 Simulation Domain Model seam |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-A4-09` |
| Diagrams | A4 domain diagrams created by `WI-A4-06` |

## Goal

Lock the A4-1 milestone with one test that runs RTS and transport/graph domains through the same
host-facing API and verifies their domain-specific outputs.

## Context to read (and only this)

- `src/OpenSage.Game/SimulationHost.cs`.
- Domain contracts and both adapters under `src/OpenSage.Game/Simulation/`.
- Existing host and transport-domain tests.
- `docs/roadmap/implementation-checklist.md`: A4-1 acceptance criterion.

## Steps

1. Build one parameterized contract test over the two domain factories.
2. Run each domain to its stop condition without caller branching.
3. Assert lifecycle completion, metrics, topology keys, and output portability.
4. Add a source check preventing `SimulationHost` from constructing a concrete domain adapter.

## Files likely to change

- New test under `src/OpenSage.Game.Tests/Simulation/`

## Acceptance criteria

- [ ] Both domains execute through identical caller code.
- [ ] Each emits its documented metrics and topology keys.
- [ ] The host has no concrete adapter construction.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SimulationDomainContract"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not compare domain outcomes or force identical state schemas.

## On completion

- Set Status above to `Done` or `Blocked`.
- Tick A4-1 in `implementation-checklist.md`.
- Append a result line to `.exchange`.
