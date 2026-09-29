# Task Card `WI-W3a-14`: Add period content definitions

| Field | Value |
|---|---|
| Card ID | `WI-W3a-14` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-13` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-13` is Done.

Add 1870 unit and weapon definitions as a Western Front scope above Generals assets.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §6 and §7.1-§7.5.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §3 and §8.2.
- `src/Panopticon.Simulation/Logic/Object/ObjectDefinition.cs:506-511`: vision-range fields.
- `src/Panopticon.Simulation/Logic/Object/Weapon/WeaponTemplate.cs:277-282`: range and scatter fields.

## Steps

1. Add domain-shipped INI definitions for French and German infantry, cavalry, guns, limbers, caissons, and headquarters.
2. Map definitions to Generals placeholder models named in W3a §6.
3. Derive engine-facing ranges, rates, ammunition, and vision from the sourced parameter JSON.
4. Load the definitions in an `AssetStore` scope above Generals.
5. Add tests that all Sedan content resolves.

## Files likely to change

- Western Front INI content files
- Western Front content loader files
- Western Front content tests

## Acceptance criteria

- [ ] Objects and weapons load in a scope above Generals.
- [ ] Placeholder models match W3a §6.
- [ ] Weapon range and `VisionRange` match sourced parameters.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~PeriodContentDefinitions"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No custom 1870 art assets.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
