# Task Card `WI-W3a-08`: Define the order-of-battle schema

| Field | Value |
|---|---|
| Card ID | `WI-W3a-08` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-05`, `WI-W1-01` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W0-05` and `WI-W1-01` are Done and the named schema and parameter types are reconciled.

Create the generic Western Front order-of-battle schema used by the Sedan datasets.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §5.1-§5.3.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §3, §4, and §6.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §3 and §9.

## Steps

1. Define schema records for formations from army through battalion, battery, and squadron.
2. Validate stable keys, echelon values, parent links, side, kind, commander, strengths, weapon set, source, and confidence.
3. Define assembly-area records with lat/lon polygons and provenance.
4. Define organization template records for battalions, batteries, squadrons, and cavalry regiments.
5. Add schema validation tests with one valid and several invalid fixtures.

## Files likely to change

- Western Front order-of-battle schema files
- Western Front data validation files
- Western Front tests

## Acceptance criteria

- [ ] Schema validates keys, echelons, parents, strengths, templates, and provenance.
- [ ] `source` and `confidence` are required on every formation and assembly-area entry.
- [ ] Invalid parent chains and duplicate keys are rejected.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~OrderOfBattleSchema"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No Sedan transcription beyond minimal test fixtures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
