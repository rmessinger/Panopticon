# Task Card `WI-W3a-19`: Implement tactical artillery

| Field | Value |
|---|---|
| Card ID | `WI-W3a-19` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-12`, `WI-W3a-14`, `WI-W3a-17`, `WI-W1-08` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-12`, `WI-W3a-14`, `WI-W3a-17`, and `WI-W1-08` are Done and logistics types are reconciled.

Represent 1870 batteries as tactical gun, crew, limber, and caisson objects with ledger-backed ammunition.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §7.2 and §7.4.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §6-§8.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §5.

## Steps

1. Spawn each battery as six gun objects with crews, limbers, caissons, and horse-team holdings.
2. Require minimum crew and unlimbered state before firing.
3. Apply limbering and unlimbering durations from parameters.
4. Move rounds from caisson to limber to gun through the ledger and W1 logistics links.
5. Transfer uncrewed captured guns and mitrailleuses as holdings.

## Files likely to change

- Western Front tactical artillery files
- Western Front logistics integration files
- Period content definitions
- Western Front tests

## Acceptance criteria

- [ ] Crew minimum, limbering, ammunition flow, and capture transfer follow W3a §7.4.
- [ ] Guns move only when limbered with horse teams.
- [ ] All artillery fire is direct and line-of-sight gated.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~TacticalArtillery"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No indirect fire or operational artillery abstraction for Sedan batteries.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
