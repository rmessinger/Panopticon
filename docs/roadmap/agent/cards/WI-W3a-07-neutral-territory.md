# Task Card `WI-W3a-07`: Enforce neutral territory

| Field | Value |
|---|---|
| Card ID | `WI-W3a-07` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-06`, `WI-W3a-12` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-06` and `WI-W3a-12` are Done.

Apply the 1870 border layer so neutral territory blocks German entry and interns French holdings.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §4 and §7.6.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §4 and §5.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §5 and §6.

## Steps

1. Read neutral cell annotations from the W3a `border` feature mapping.
2. Reject German movement paths that enter neutral cells.
3. Move French personnel, horses, field guns, mitrailleuses, and ammunition entering neutral cells to the `interned` sink or holding rules from `WI-W3a-12`.
4. Record internment events with source cell and formation key.
5. Add synthetic border tests for blocked entry and internment.

## Files likely to change

- Western Front movement and neutral territory files
- Western Front ledger files
- Western Front tests

## Acceptance criteria

- [ ] German movement into neutral territory is blocked.
- [ ] French soldiers, horses, and guns entering neutral territory move to `interned`.
- [ ] Ledger conservation remains exact after internment.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NeutralTerritory"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No diplomatic model beyond the border rule.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
