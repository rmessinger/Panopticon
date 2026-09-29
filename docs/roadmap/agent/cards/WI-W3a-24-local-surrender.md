# Task Card `WI-W3a-24`: Add local surrender

| Field | Value |
|---|---|
| Card ID | `WI-W3a-24` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-07`, `WI-W3a-12`, `WI-W1-12` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-07`, `WI-W3a-12`, and `WI-W1-12` are Done.

Implement encirclement detection and platoon-level surrender into the captured ledger flow.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §7.6 and §9.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §8.3.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §4 and §5.

## Steps

1. Add an encirclement query that searches paths through regions not held by the enemy to exits.
2. Treat the western extent edge toward Mézières and neutral territory as exits.
3. Evaluate local surrender for encircled platoons below cohesion threshold and near enemy soldiers.
4. Move surrendered personnel to `captured` by captor side and transfer equipment holdings.
5. Add tests for open path, encircled no surrender, and valid surrender.

## Files likely to change

- Western Front encirclement files
- Western Front surrender policy files
- Western Front tests

## Acceptance criteria

- [ ] Encirclement depends on path access to valid exits.
- [ ] Surrendered soldiers move to `captured`.
- [ ] Equipment transfers to the captor as holdings.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~LocalSurrender"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No army-level capitulation decision in this card.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
