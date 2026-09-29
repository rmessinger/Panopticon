# Task Card `WI-W3a-21`: Add courier and telegraph links

| Field | Value |
|---|---|
| Card ID | `WI-W3a-21` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-04`, `WI-W3a-06` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-04` and `WI-W3a-06` are Done.

Extend W1 communication links with 1870 mounted courier and field telegraph behavior.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §8.1.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §4.1-§4.2 and §9.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §5.

## Steps

1. Add mounted courier comm type with path-length latency, staff delay, and enemy-presence loss chance.
2. Add field telegraph comm type with fixed short delay and cut state when either endpoint region is enemy-held.
3. Make telegraph availability depend on dataset line features for German royal and army headquarters.
4. Disable telephone and wireless in the 1870 parameter set.
5. Add delivery, disruption, fallback, and metric tests.

## Files likely to change

- Western Front communication files
- Sedan communication parameter files
- Western Front tests

## Acceptance criteria

- [ ] Courier latency and disruption follow W3a §8.1.
- [ ] Telegraph links cut when either endpoint region is enemy-held.
- [ ] Telephone and wireless are disabled for Sedan.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SedanCommunication"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No telephone, wireless, or player communication controls.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
