# Task Card `WI-W3a-04`: Define the topology builder contract

| Field | Value |
|---|---|
| Card ID | `WI-W3a-04` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-03`, `WI-W0-03`, `WI-B4b-18` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-03`, `WI-W0-03`, and `WI-B4b-18` are Done and the named topology and payload types are reconciled.

Create `WesternFrontTopologyBuilder` and its build result contract.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §3-§4.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §3 and §5.

## Steps

1. Add the builder interface and result records.
2. Represent source references on sectors, regions, cells, nodes, and links.
3. Sort source features deterministically by source id and geometry order.
4. Add tests against a minimal synthetic payload.

## Files likely to change

- Western Front topology builder files
- Western Front tests

## Acceptance criteria

- [ ] Builder output contains sectors, regions, cells, nodes, links, and source references.
- [ ] Two builds from the same payload produce identical keys and ordering.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontTopologyBuilder"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No movement, logistics, or combat systems.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
