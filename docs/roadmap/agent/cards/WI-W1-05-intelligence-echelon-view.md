# Task Card `WI-W1-05`: Add intelligence pictures and echelon views

| Field | Value |
|---|---|
| Card ID | `WI-W1-05` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-04` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-04` is Done.

Implement `IntelligencePicture`, contact records, and the restricted `EchelonView` input to policies.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §4.3.
- W1 message and contact schema from `WI-W1-02`.
- `docs/roadmap/deep-dives/a4-pluggable-ai.md`: policy seam context.

## Steps

1. Add `IntelligencePicture`, `EnemyContact`, `ObservationSource`, and `EchelonView`.
2. Implement `IntelligenceSystem` to update contacts from own-region contact, reports, observation assets, and wireless interception.
3. Compute force ratio from `IntelligencePicture` only.
4. Add compile-time API tests or unit tests proving policy inputs do not expose full scenario state.
5. Add tests for stale observations and confidence decay.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Intelligence/*.cs`
- `src/Panopticon.Simulation.WesternFront/Systems/IntelligenceSystem.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/IntelligenceSystemTests.cs`

## Acceptance criteria

- [ ] `EchelonView` contains self state, reports, picture, and received orders only.
- [ ] Force ratio uses contact estimates and confidence.
- [ ] Stale contacts remain reconstructable through schema v2.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~IntelligenceSystem"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No command policy implementation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
