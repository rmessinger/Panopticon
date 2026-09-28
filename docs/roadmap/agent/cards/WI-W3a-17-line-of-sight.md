# Task Card `WI-W3a-17`: Require line of sight

| Field | Value |
|---|---|
| Card ID | `WI-W3a-17` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-06`, `WI-W3a-15` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-06` and `WI-W3a-15` are Done and terrain height access is reconciled with source.

Gate direct fire and detection on terrain and feature line of sight.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §4, §7.1, and §7.3.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.1-§5.3.
- `src/Panopticon.Simulation/Logic/Object/Weapon/Weapon.cs:157` and `ObjectDefinition.cs:506-511`: fire and detection hooks.

## Steps

1. Add `LineOfSightService` over generated height samples and W3a feature cover classes.
2. Run a cell-resolution pass before sample-resolution checks.
3. Treat woods and villages as occluders beyond cover-dependent depth.
4. Consult line of sight from fire resolution and detection.
5. Add tests for terrain block, feature block, clear line, and visibility cap hooks.

## Files likely to change

- Western Front line-of-sight files
- Period hit resolver files
- Detection integration files
- Western Front tests

## Acceptance criteria

- [ ] Fire requires line of sight.
- [ ] Detection requires line of sight.
- [ ] Woods and village occlusion apply by cover depth.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~LineOfSight"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No 3-D renderer occlusion or visibility debugging UI.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
