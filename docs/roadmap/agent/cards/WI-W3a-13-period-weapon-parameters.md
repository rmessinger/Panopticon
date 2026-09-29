# Task Card `WI-W3a-13`: Verify period weapon parameters

| Field | Value |
|---|---|
| Card ID | `WI-W3a-13` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-W1-01` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-01` is Done and parameter JSON format is reconciled with source.

Create sourced 1870 weapon, crew, limbering, and horse-loss parameter JSON.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §6, §7.1-§7.5, and §12.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §8.2-§8.3.
- Public-domain sources named in W3a §5.1, plus their weapon tables where available.

## Steps

1. Verify or correct the W3a §6 weapon table for Chassepot, Dreyse, Krupp guns, La Hitte guns, and Reffye mitrailleuse.
2. Store every parameter as `{ value, unit }` plus `source`.
3. Add hit tables by range band, posture, cover, and fire mode.
4. Add La Hitte fuze presets, crew minimums, limbering times, and horse casualty fractions.
5. Record confidence notes for derived or estimated values.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Data/Sedan/PeriodWeaponParameters.json`
- Western Front parameter validation tests

## Acceptance criteria

- [ ] Every parameter value has unit and source.
- [ ] W3a §6 weapon values are verified or corrected.
- [ ] Hit tables cover range band, posture, cover, and fire mode.
- [ ] La Hitte fuze presets, crew minimums, limbering times, and horse fractions are present.

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~PeriodWeaponParameters"
```

## Out of scope / guardrails

No runtime weapon behavior changes. Derived estimates must be labelled, not hidden.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
