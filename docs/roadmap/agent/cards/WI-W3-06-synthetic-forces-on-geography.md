# Task Card `WI-W3-06`: Place synthetic W forces on generated geography

| Field | Value |
|---|---|
| Card ID | `WI-W3-06` |
| Roadmap phase | [Track W · W3](../../track-w-western-front.md): geospatial sector |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3-05`, `WI-W1-07` |
| Diagrams | [W3 §6](../../deep-dives/w3-geospatial-sector.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3-05` and W1 scenario evidence are Done and scenario options are reconciled.

Create the generated-sector scenario builder for the existing W1/W2 synthetic forces.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3-geospatial-sector.md` §4.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §10.

## Steps

1. Add scenario options that accept a generated topology.
2. Place blue and red formations along the front bands.
3. Attach depots to rear rail or road nodes.
4. Preserve synthetic quantities and parameter values from W1.
5. Add scenario construction tests.

## Files likely to change

- Western Front scenario files
- Western Front tests

## Acceptance criteria

- [ ] Scenario creates valid formations, depots, links, and initial fronts on synthetic generated topology.
- [ ] No historical OOB data is required.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GeneratedSectorScenario"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No operator data and no W6 historical OOB.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
