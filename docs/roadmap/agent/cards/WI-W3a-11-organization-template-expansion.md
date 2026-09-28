# Task Card `WI-W3a-11`: Expand period organization templates

| Field | Value |
|---|---|
| Card ID | `WI-W3a-11` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-09`, `WI-W3a-10` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-09` and `WI-W3a-10` are Done.

Implement deterministic expansion from battalions, squadrons, and batteries to the platoon-floor records.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §5.3 and §13.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §3, §4, and §6.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §4 and §6.

## Steps

1. Add `OrderOfBattleExpander` for Sedan organization templates.
2. Expand Prussian battalions, French battalions, chasseurs, Jäger, cavalry regiments, and batteries through declared template paths.
3. Allocate nominal and estimated field strengths by largest remainder with stable ordering.
4. Generate stable child keys by appending template path segments.
5. Add determinism and conservation tests.

## Files likely to change

- Western Front order-of-battle expansion files
- Sedan organization template JSON
- Western Front tests

## Acceptance criteria

- [ ] Expansion reaches Zug, section, cavalry troop, and gun-section floor records.
- [ ] Generated keys are stable across runs.
- [ ] Expanded personnel and equipment totals match source entries exactly.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~OrderOfBattleExpander"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No tactical object spawning or scenario placement.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
