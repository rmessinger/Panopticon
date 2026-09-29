# Task Card `WI-W3a-25`: Add army capitulation

| Field | Value |
|---|---|
| Card ID | `WI-W3a-25` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-24` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-24` is Done.

Implement French army capitulation policy and final outcome recording for Sedan.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §9 and §11.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §8.3.
- `src/Panopticon.Simulation/Logic/GameOutcome.cs:5-20`: outcome status and reason.

## Steps

1. Add `CapitulationPolicy` using encircled personnel fraction, army cohesion, and breakout feasibility thresholds.
2. Apply a negotiation delay before final surrender effects.
3. Move every French formation in the encirclement to `captured` and transfer equipment holdings.
4. Set `GameOutcome` to `Decided` with `OutcomeReason.Surrendered` after capitulation.
5. Record 2 September 12:00 without capitulation as `InProgress`.

## Files likely to change

- Western Front capitulation policy files
- Sedan scenario outcome files
- Western Front tests

## Acceptance criteria

- [ ] Capitulation sets P3 `GameOutcome` to `Decided` with `OutcomeReason.Surrendered` after negotiation delay.
- [ ] 2 September 12:00 without capitulation records `InProgress`.
- [ ] No score decision applies to Sedan window end.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ArmyCapitulation"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No scripted historical white flag or signing time.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
