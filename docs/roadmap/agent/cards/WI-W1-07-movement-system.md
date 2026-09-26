# Task Card `WI-W1-07`: Add operational movement and stacking

| Field | Value |
|---|---|
| Card ID | `WI-W1-07` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-06`, `WI-W0-03` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-06` and `WI-W0-03` are Done.

Move formations over the road graph with fatigue and region capacity.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §5.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §5.
- W0 topology and formation state types.

## Steps

1. Add movement order interpretation for region paths.
2. Add march-rate parameters by echelon and ground-state modifiers.
3. Enforce stacking capacity per side and region.
4. Increase fatigue from movement and blocked waiting.
5. Add tests for route following, blocked capacity, fatigue, and attack-region handoff.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Systems/MovementSystem.cs`
- `src/Panopticon.Simulation.WesternFront/Movement/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/MovementSystemTests.cs`

## Acceptance criteria

- [ ] A formation advances one region only when elapsed movement progress reaches link cost.
- [ ] Movement into enemy-occupied regions creates or preserves an attack state for combat.
- [ ] Stacking prevents over-capacity moves.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~MovementSystem"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No combat resolution.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
