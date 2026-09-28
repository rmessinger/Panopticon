# Task Card `WI-W3a-16`: Model shells and fuzes

| Field | Value |
|---|---|
| Card ID | `WI-W3a-16` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-15` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-15` is Done.

Add period shell burst behavior for percussion, shrapnel, case, and La Hitte time fuzes.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §7.2 and §7.4.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §8.2.
- `src/Panopticon.Simulation/Logic/Object/GameObject.cs:272`: damage entry point.
- `src/Panopticon.Simulation/Logic/Object/Damage/DamageInfo.cs:38-130`: damage payload.

## Steps

1. Add period shell kind parameters and select behavior from the weapon definition.
2. Apply percussion dispersion from aim point with seeded range-based spread.
3. Apply shrapnel and case bursts through `DamageInfoInput` areas.
4. Enforce La Hitte two-preset time fuze behavior and percussion fallback.
5. Add tests for each shell and fuze case.

## Files likely to change

- Western Front shell and fuze model files
- Period weapon parameter files
- Western Front tests

## Acceptance criteria

- [ ] Percussion impacts disperse with range and damage within lethal radius.
- [ ] Shrapnel and case bursts match the parameter model.
- [ ] La Hitte preset behavior matches W3a §7.2.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ShellsAndFuzes"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No indirect fire or ballistic simulation beyond the direct-fire burst model.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
