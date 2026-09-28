# Task Card `WI-W6-01`: Define the W6 campaign file

| Field | Value |
|---|---|
| Card ID | `WI-W6-01` |
| Roadmap phase | [Track W · W6](../../track-w-western-front.md): long-run campaign experiment |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | W3b evidence, W4 result schema, W5 shard config cards |
| Diagrams | [W6 §6](../../deep-dives/w6-long-run-campaign.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once W3b evidence, W4 result schema, and W5 shard config cards are Done.

Create the versioned W6 campaign definition.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w6-long-run-campaign.md` §2.
- `docs/roadmap/deep-dives/w3b-somme-sector.md` §2.

## Steps

1. Define campaign JSON schema version 1.
2. Bind sector payload, topology key, parameter set, shard layout, dates, seed, and checkpoint cadence.
3. Add JSON round-trip and validation tests.

## Files likely to change

- Western Front campaign definition files
- Tests

## Acceptance criteria

- [ ] Campaign definition round-trips with all required bindings.
- [ ] Invalid dates, missing sector, or missing parameter hash fail validation.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontCampaignDefinition"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No historical OOB parser.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
