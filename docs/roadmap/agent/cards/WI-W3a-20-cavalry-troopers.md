# Task Card `WI-W3a-20`: Implement cavalry troopers

| Field | Value |
|---|---|
| Card ID | `WI-W3a-20` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-12`, `WI-W3a-14` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-12` and `WI-W3a-14` are Done.

Add cavalry troopers with horse holdings, dismounting, carbine fallback, and charge shock.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §6, §7.5, and §7.6.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §4.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §4-§6.

## Steps

1. Represent each cavalry trooper as one soldier object carrying one `horse` holding.
2. Configure sabre, lance, or carbine weapon sets by cavalry kind.
3. Dismount troopers after horse loss, reduce movement speed, and restrict weapons to carbine where applicable.
4. Add charge shock parameters against unformed or disordered targets.
5. Draw horse casualties per hit by the parameter fraction.

## Files likely to change

- Western Front cavalry behavior files
- Period content definitions
- Period weapon parameter JSON
- Western Front tests

## Acceptance criteria

- [ ] Horse holdings conserve exactly.
- [ ] Horse loss dismounts troopers with reduced speed and weapon restrictions.
- [ ] Charges apply the configured shock bonus only against valid targets.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~CavalryTrooper"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No detailed mounted animation or custom horse model work.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
