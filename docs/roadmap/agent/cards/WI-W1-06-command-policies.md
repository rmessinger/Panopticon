# Task Card `WI-W1-06`: Add command policy interface and reactive rules

| Field | Value |
|---|---|
| Card ID | `WI-W1-06` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-05` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-05` is Done.

Add echelon-specific rule policies that react to force ratio, orders, stock, and morale.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §4.4.
- `docs/roadmap/deep-dives/a4-pluggable-ai.md`: policy selection pattern.
- W1 `EchelonView` from `WI-W1-05`.

## Steps

1. Add `IFormationCommandPolicy` and `CommandDecision`.
2. Add rule policies for army, corps, division, brigade, battalion, and platoon.
3. Implement `CommandSystem` to build `EchelonView`, invoke the policy for each headquarters, and enqueue orders or reports.
4. Use force-ratio thresholds from parameters, not scripted attack times.
5. Add tests for attack, hold, reserve allocation, support request, and withdrawal decisions.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Command/*.cs`
- `src/Panopticon.Simulation.WesternFront/Systems/CommandSystem.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/CommandPolicyTests.cs`

## Acceptance criteria

- [ ] Attack decisions change when `IntelligencePicture` force ratio crosses thresholds.
- [ ] Policies never receive direct access to `WesternFrontScenario`.
- [ ] Platoon-level orders are emitted for aggregate combat.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~CommandPolicy"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No movement, combat, or logistics execution.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
